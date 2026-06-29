using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HaloCreek.Infrastructure;
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
        private readonly Timer _refreshTimer;
        private TSnapshot _current;
        private RefreshState _state;
        private SnapshotRefreshReason _pendingReason;// 调试信息 不讲究地只记录第一个pending请求的触发方
        private bool _hasSuccessfulRefresh;

        public WorkspaceSnapshotStore()
        {
            var workspace = WorkspaceRuntime.Current;
            _current = TSnapshot.CreateEmpty(workspace);
            _refreshTimer = new Timer(
                OnRefreshTimerTick,
                state: null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);

            WorkspaceRuntime.Changed += OnWorkspaceChanged;
            RequestRefresh(SnapshotRefreshReason.WorkspaceChanged);
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

            WorkspaceRuntime.Changed -= OnWorkspaceChanged;
            _refreshTimer.Dispose();
        }

        private void OnWorkspaceChanged(WorkspaceContext workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            lock (_lock)
            {
                if (_state == RefreshState.Disposed)
                {
                    return;
                }

                _current = TSnapshot.CreateEmpty(workspace);
                _hasSuccessfulRefresh = false;
            }

            PublishChanged(workspace);
            // todo 这里不应该温柔request 应该停掉之前的
            RequestRefresh(SnapshotRefreshReason.WorkspaceChanged);
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
                    var snapshot = TSnapshot.ReadSnapshot(workspace);
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
                if (_state == RefreshState.Disposed
                    || !IsCurrentWorkspace(workspace))
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
            PublishChanged(workspace);
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

        private void PublishChanged(WorkspaceContext expectedWorkspace)
        {
            Dispatcher.UIThread.Post(() =>
            {
                lock (_lock)
                {
                    if (_state == RefreshState.Disposed || !IsCurrentWorkspace(expectedWorkspace))
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

        private static bool IsCurrentWorkspace(WorkspaceContext expectedWorkspace)
        {
            var currentWorkspace = WorkspaceRuntime.Current;
            return PlatformInfrastructure.AreWorkspacePathsEquivalent(
                    expectedWorkspace.WorkspacePath,
                    currentWorkspace.WorkspacePath)
                && PlatformInfrastructure.AreWorkspacePathsEquivalent(
                    expectedWorkspace.GitRootPath,
                    currentWorkspace.GitRootPath);
        }

        private enum RefreshState
        {
            Idle,
            Refreshing,
            RefreshingWithPending,
            Disposed,
        }
    }
}
