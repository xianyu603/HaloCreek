using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HaloCreek.Logging;

namespace HaloCreek.Services.WorkspaceSnapshots
{
    public static class WorkspaceSnapshotStore
    {
        public static WorkspaceSnapshotStore<TSnapshot> Create<TSnapshot>()
            where TSnapshot : IWorkspaceSnapshot<TSnapshot>
        {
            return new WorkspaceSnapshotStore<TSnapshot>(TSnapshot.ReadSnapshot);
        }

        public static WorkspaceSnapshotStore<TSnapshot> Create<TSnapshot>(string key)
            where TSnapshot : IKeyedWorkspaceSnapshot<TSnapshot>
        {
            ArgumentNullException.ThrowIfNull(key);
            return new WorkspaceSnapshotStore<TSnapshot>(() => TSnapshot.ReadSnapshot(key));
        }
    }

    public sealed class WorkspaceSnapshotStore<TSnapshot> :
        IWorkspaceSnapshotSource<TSnapshot>,
        IDisposable
        where TSnapshot : IWorkspaceSnapshot<TSnapshot>
    {
        private const string LogCategory = "WorkspaceSnapshot";
        private static readonly TimeSpan FileSystemChangeDebounce = TimeSpan.FromMilliseconds(500);

        private readonly object _lock = new();
        private readonly Func<TSnapshot> _readSnapshot;
        private readonly Timer _refreshTimer;
        private readonly Timer _fileSystemWatcherDebounceTimer;
        private TSnapshot _current;
        private FileSystemWatcher? _fileSystemWatcher;
        private string? _fileSystemListenPath;
        private RefreshState _state;
        private SnapshotRefreshReason _pendingReason;// 调试信息 不讲究地只记录第一个pending请求的触发方
        private bool _hasSuccessfulRefresh;

        internal WorkspaceSnapshotStore(Func<TSnapshot> readSnapshot)
        {
            _readSnapshot = readSnapshot ?? throw new ArgumentNullException(nameof(readSnapshot));
            _current = TSnapshot.CreateEmpty();
            _refreshTimer = new Timer(
                OnRefreshTimerTick,
                state: null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _fileSystemWatcherDebounceTimer = new Timer(
                OnFileSystemChangeDebounceElapsed,
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
                _fileSystemWatcherDebounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            RemoveFileSystemWatcher();
            _fileSystemWatcherDebounceTimer.Dispose();
            _refreshTimer.Dispose();
        }

        private void OnRefreshTimerTick(object? state)
        {
            RequestRefresh(SnapshotRefreshReason.Timer);
        }

        private void OnFileSystemChangeDebounceElapsed(object? state)
        {
            RequestRefresh(SnapshotRefreshReason.FileSystemChanged);
        }

        private void ScheduleNextTimerTick()
        {
            var refreshJitterMilliseconds = (int)TSnapshot.SnapshotRefreshJitter.TotalMilliseconds;
            var dueTime = TSnapshot.SnapshotRefreshInterval + TimeSpan.FromMilliseconds(
                refreshJitterMilliseconds > 0
                    ? Random.Shared.Next(0, refreshJitterMilliseconds + 1)
                    : 0);

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
                    RemoveFileSystemWatcher();
                    var snapshot = _readSnapshot();
                    UpdateFileSystemWatcher(workspace, snapshot.SnapshotListenPath);
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

        private void OnFileSystemWatcherChanged(object sender, FileSystemEventArgs e)
        {
            ScheduleFileSystemChangedRefresh();
        }

        private void OnFileSystemWatcherRenamed(object sender, RenamedEventArgs e)
        {
            ScheduleFileSystemChangedRefresh();
        }

        private void OnFileSystemWatcherError(object sender, ErrorEventArgs e)
        {
            Log.Warning(
                LogCategory,
                $"Snapshot file watcher failed. Snapshot={typeof(TSnapshot).Name}, Path={GetFileSystemListenPath()}, Error={e.GetException()}");
            ScheduleFileSystemChangedRefresh();
        }

        private void ScheduleFileSystemChangedRefresh()
        {
            lock (_lock)
            {
                if (_state == RefreshState.Disposed)
                {
                    return;
                }

                _fileSystemWatcherDebounceTimer.Change(
                    FileSystemChangeDebounce,
                    Timeout.InfiniteTimeSpan);
            }
        }

        private void UpdateFileSystemWatcher(
            WorkspaceContext workspace,
            string? listenPath)
        {
            if (string.IsNullOrWhiteSpace(listenPath))
            {
                return;
            }

            var trimmedListenPath = listenPath.Trim();
            FileSystemWatcher? watcher = null;
            try
            {
                watcher = CreateFileSystemWatcher(trimmedListenPath);
                watcher.Changed += OnFileSystemWatcherChanged;
                watcher.Created += OnFileSystemWatcherChanged;
                watcher.Deleted += OnFileSystemWatcherChanged;
                watcher.Renamed += OnFileSystemWatcherRenamed;
                watcher.Error += OnFileSystemWatcherError;

                var shouldDispose = false;
                lock (_lock)
                {
                    if (_state == RefreshState.Disposed)
                    {
                        shouldDispose = true;
                    }
                    else
                    {
                        _fileSystemWatcher = watcher;
                        _fileSystemListenPath = trimmedListenPath;
                    }
                }

                if (shouldDispose)
                {
                    DisposeFileSystemWatcher(watcher);
                    return;
                }

                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException
                or NotSupportedException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException
                or PathTooLongException)
            {
                if (watcher is not null)
                {
                    lock (_lock)
                    {
                        if (ReferenceEquals(_fileSystemWatcher, watcher))
                        {
                            _fileSystemWatcher = null;
                            _fileSystemListenPath = null;
                        }
                    }

                    DisposeFileSystemWatcher(watcher);
                }

                Log.Warning(
                    LogCategory,
                    $"Snapshot file watcher could not start. Snapshot={typeof(TSnapshot).Name}, Path={trimmedListenPath}, Workspace={workspace.WorkspacePath}, Error={ex.Message}");
                return;
            }

            Log.Debug(
                LogCategory,
                $"Snapshot file watcher started. Snapshot={typeof(TSnapshot).Name}, Path={trimmedListenPath}, Workspace={workspace.WorkspacePath}");
        }

        private FileSystemWatcher CreateFileSystemWatcher(string listenPath)
        {
            var normalizedPath = Path.GetFullPath(listenPath.Trim());
            FileSystemWatcher watcher;
            if (Directory.Exists(normalizedPath))
            {
                watcher = new FileSystemWatcher(normalizedPath)
                {
                    IncludeSubdirectories = true,
                };
            }
            else
            {
                var directoryPath = Path.GetDirectoryName(normalizedPath);
                if (string.IsNullOrWhiteSpace(directoryPath)
                    || !Directory.Exists(directoryPath))
                {
                    throw new DirectoryNotFoundException(
                        $"Snapshot listen directory does not exist. Path={normalizedPath}");
                }

                watcher = new FileSystemWatcher(
                    directoryPath,
                    Path.GetFileName(normalizedPath));
            }

            watcher.NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.CreationTime;
            return watcher;
        }

        private void RemoveFileSystemWatcher()
        {
            FileSystemWatcher? watcher;
            lock (_lock)
            {
                watcher = _fileSystemWatcher;
                _fileSystemWatcher = null;
                _fileSystemListenPath = null;
            }

            if (watcher is null)
            {
                return;
            }

            DisposeFileSystemWatcher(watcher);
        }

        private string? GetFileSystemListenPath()
        {
            lock (_lock)
            {
                return _fileSystemListenPath;
            }
        }

        private void DisposeFileSystemWatcher(FileSystemWatcher watcher)
        {
            watcher.Changed -= OnFileSystemWatcherChanged;
            watcher.Created -= OnFileSystemWatcherChanged;
            watcher.Deleted -= OnFileSystemWatcherChanged;
            watcher.Renamed -= OnFileSystemWatcherRenamed;
            watcher.Error -= OnFileSystemWatcherError;
            watcher.Dispose();
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
    }
}
