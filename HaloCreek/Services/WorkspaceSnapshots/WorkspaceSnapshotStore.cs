using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HaloCreek.Logging;

namespace HaloCreek.Services.WorkspaceSnapshots
{
    public sealed class WorkspaceSnapshotStore<TSnapshot> :
        IWorkspaceSnapshotSource<TSnapshot>,
        IDisposable
        where TSnapshot : IWorkspaceSnapshot<TSnapshot>
    {
        private const string LogCategory = "WorkspaceSnapshot";
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RefreshJitter = TimeSpan.FromSeconds(2);

        private readonly object _lock = new();
        private readonly Func<TSnapshot> _readSnapshot;
        private readonly Timer _refreshTimer;
        private TSnapshot _current;
        private RefreshState _state;
        private SnapshotRefreshReason _pendingReason;// 调试信息 不讲究地只记录第一个pending请求的触发方
        private bool _hasSuccessfulRefresh;

        public WorkspaceSnapshotStore()
            : this(CreateDefaultReader())
        {
        }

        public WorkspaceSnapshotStore(string key)
            : this(CreateKeyedReader(key))
        {
        }

        private WorkspaceSnapshotStore(Func<TSnapshot> readSnapshot)
        {
            var workspace = WorkspaceRuntime.Current;
            _readSnapshot = readSnapshot ?? throw new ArgumentNullException(nameof(readSnapshot));
            _current = TSnapshot.CreateEmpty();
            _refreshTimer = new Timer(
                OnRefreshTimerTick,
                state: null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);

            RequestRefresh(SnapshotRefreshReason.Init);
            ScheduleNextTimerTick();
        }

        public event EventHandler? Changed;

        public TSnapshot Current
        {
            get
            {
                lock (_lock)
                {
                    return _current;
                }
            }
        }

        public bool HasSuccessfulRefresh
        {
            get
            {
                lock (_lock)
                {
                    return _hasSuccessfulRefresh;
                }
            }
        }

        public void RequestRefresh(SnapshotRefreshReason reason)
        {
            bool shouldRefresh = false;
            lock (_lock)
            {
                if (_state == RefreshState.Idle)
                {
                    _state = RefreshState.Refreshing;
                    shouldRefresh = true;
                }
                else if (_state == RefreshState.Refreshing)
                {
                    _pendingReason = reason;
                    _state = RefreshState.RefreshingWithPending;
                }
            }
            if (shouldRefresh)
            {
                _ = Task.Run(() => RunRefreshLoop(reason));
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_state == RefreshState.Disposed)
                {
                    return;
                }

                _state = RefreshState.Disposed;
                _refreshTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            _refreshTimer.Dispose();
        }

        private void OnRefreshTimerTick(object? state)
        {
            RequestRefresh(SnapshotRefreshReason.Timer);
        }

        private void ScheduleNextTimerTick()
        {
            var dueTime = RefreshInterval + TimeSpan.FromMilliseconds(
                Random.Shared.Next(0, (int)RefreshJitter.TotalMilliseconds + 1));

            lock (_lock)
            {
                if (_state == RefreshState.Disposed)
                {
                    return;
                }

                _refreshTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
            }
        }

        private void RunRefreshLoop(SnapshotRefreshReason reason)
        {
            while (true)
            {
                var workspace = WorkspaceRuntime.Current;

                try
                {
                    var snapshot = _readSnapshot();
                    PublishRefreshResult(workspace, snapshot, reason);
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        LogCategory,
                        $"Snapshot refresh failed. Snapshot={typeof(TSnapshot).Name}, Reason={reason}, Workspace={workspace.WorkspacePath}, Error={ex}");
                }

                if (!CompleteRefreshAndGetIsPending(out reason))
                {
                    ScheduleNextTimerTick();
                    return;
                }
            }
        }

        private void PublishRefreshResult(
            WorkspaceContext workspace,
            TSnapshot snapshot,
            SnapshotRefreshReason reason)
        {
            var shouldPublishChanged = false;
            lock (_lock)
            {
                if (_state == RefreshState.Disposed)
                {
                    return;
                }

                var contentChanged = !TSnapshot.ContentEquals(_current, snapshot);
                if (contentChanged)
                {
                    _current = snapshot;
                }

                shouldPublishChanged = contentChanged || !_hasSuccessfulRefresh;
                _hasSuccessfulRefresh = true;
            }

            if (!shouldPublishChanged)
            {
                return;
            }

            Log.Debug(
                LogCategory,
                $"Snapshot published. Snapshot={typeof(TSnapshot).Name}, Reason={reason}, Workspace={workspace.WorkspacePath}");
            PublishChanged();
        }

        private bool CompleteRefreshAndGetIsPending(out SnapshotRefreshReason reason)
        {
            lock (_lock)
            {
                if (_state == RefreshState.Disposed)
                {
                    reason = default;
                    return false;
                }

                if (_state == RefreshState.RefreshingWithPending)
                {
                    reason = _pendingReason;
                    _state = RefreshState.Refreshing;
                    return true;
                }

                _state = RefreshState.Idle;
                reason = default;
                return false;
            }
        }

        private void PublishChanged()
        {
            Dispatcher.UIThread.Post(() =>
            {
                lock (_lock)
                {
                    if (_state == RefreshState.Disposed)
                    {
                        return;
                    }
                }

                try
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error(LogCategory, ex, "Snapshot changed subscriber failed.");
                }
            });
        }

        private enum RefreshState
        {
            Idle,
            Refreshing,
            RefreshingWithPending,
            Disposed,
        }

        private static Func<TSnapshot> CreateDefaultReader()
        {
            if (IsKeyedSnapshotType())
            {
                throw new InvalidOperationException(
                    $"{typeof(TSnapshot).Name} requires a key. Use WorkspaceSnapshotStore<{typeof(TSnapshot).Name}>(string key).");
            }

            return TSnapshot.ReadSnapshot;
        }

        private static Func<TSnapshot> CreateKeyedReader(string key)
        {
            ArgumentNullException.ThrowIfNull(key);

            var snapshotType = typeof(TSnapshot);
            if (!IsKeyedSnapshotType())
            {
                throw new InvalidOperationException(
                    $"{snapshotType.Name} does not support keyed reads. Use the parameterless WorkspaceSnapshotStore<{snapshotType.Name}> constructor.");
            }

            var readSnapshot = snapshotType.GetMethod(
                nameof(IWorkspaceSnapshot<TSnapshot>.ReadSnapshot),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null);
            if (readSnapshot is null || readSnapshot.ReturnType != snapshotType)
            {
                throw new InvalidOperationException(
                    $"{snapshotType.Name} must expose public static {snapshotType.Name} ReadSnapshot(string key).");
            }

            return () => (TSnapshot)readSnapshot.Invoke(null, new object[] { key })!;
        }

        private static bool IsKeyedSnapshotType()
        {
            var snapshotType = typeof(TSnapshot);
            var keyedSnapshotType = typeof(IKeyedWorkspaceSnapshot<>).MakeGenericType(snapshotType);
            return keyedSnapshotType.IsAssignableFrom(snapshotType);
        }
    }
}
