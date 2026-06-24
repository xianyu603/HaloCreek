using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HaloCreek.Services;

namespace HaloCreek.Services.SessionHistory
{
    // 因为占用了系统资源(timer) 所以要写IDisposable
    // TODO: 后续把该类重命名为更贴近内部实现定位的 refresher 名称。
    public sealed class SessionHistoryRefreshService : IDisposable
    {
        private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(10);

        private enum RefreshState
        {
            Idle,
            Scheduled,
            Refreshing,
            Disposed
        }

        private readonly SessionHistoryQueryService _queryService;
        private readonly TimeSpan _refreshInterval;
        private readonly Timer _refreshTimer;
        private readonly object _lock = new();
        private string? _workspacePath;
        private int _maxSessionHistoryFiles;
        private RefreshState _state = RefreshState.Idle;

        public SessionHistoryRefreshService(
            SessionHistoryQueryService queryService,
            TimeSpan? refreshInterval = null)
        {
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
            _refreshInterval = refreshInterval ?? DefaultRefreshInterval;
            if (_refreshInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(refreshInterval), "Refresh interval must be positive.");
            }

            _refreshTimer = new Timer(
                OnRefreshTimerTick,
                state: null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
        }

        public SessionHistoryRefreshService(ISessionHistoryReader reader)
            : this(new SessionHistoryQueryService(reader))
        {
        }

        public event Action<SessionHistoryRefreshResult>? RefreshCompleted;

        private void ApplyWorkspaceTarget(WorkspaceContext workspaceContext)
        {
            ArgumentNullException.ThrowIfNull(workspaceContext);

            var shouldStartRefresh = false;
            string? refreshWorkspacePath = null;
            var refreshMaxSessionHistoryFiles = 0;

            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_state == RefreshState.Disposed, this);

                _workspacePath = workspaceContext.WorkspacePath;
                _maxSessionHistoryFiles = workspaceContext.EffectiveConfig.MaxSessionHistoryFiles;
                _refreshTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                if (_state != RefreshState.Refreshing)
                {
                    _state = RefreshState.Refreshing;
                    refreshWorkspacePath = _workspacePath;
                    refreshMaxSessionHistoryFiles = _maxSessionHistoryFiles;
                    shouldStartRefresh = true;
                }
            }

            if (shouldStartRefresh)
            {
                _ = RefreshAsync(refreshWorkspacePath!, refreshMaxSessionHistoryFiles);
            }
        }

        public void StartRefresh()
        {
            WorkspaceRuntime.Changed += OnWorkspaceChanged;
            ApplyWorkspaceTarget(WorkspaceRuntime.Current);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_state == RefreshState.Disposed)
                {
                    return;// 入乡随俗
                }
                _state = RefreshState.Disposed;
                _refreshTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            _refreshTimer.Dispose();
            WorkspaceRuntime.Changed -= OnWorkspaceChanged;
        }

        private void OnWorkspaceChanged(WorkspaceContext workspaceContext)
        {
            // TODO: Workspace 切换时当前版本不立即清空旧列表；
            // 后续如果需要避免短暂展示旧 workspace history，在这里引入 reset/refresh-started 通知。
            ApplyWorkspaceTarget(workspaceContext);
        }

        private void OnRefreshTimerTick(object? state)
        {
            string? refreshWorkspacePath;
            int refreshMaxSessionHistoryFiles;

            lock (_lock)
            {
                if (_state != RefreshState.Scheduled)
                {
                    return;
                }

                _refreshTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                if (string.IsNullOrWhiteSpace(_workspacePath))
                {
                    _state = RefreshState.Idle;
                    return;
                }

                _state = RefreshState.Refreshing;
                refreshWorkspacePath = _workspacePath;
                refreshMaxSessionHistoryFiles = _maxSessionHistoryFiles;
            }

            _ = RefreshAsync(refreshWorkspacePath, refreshMaxSessionHistoryFiles);
        }

        private async Task RefreshAsync(string workspacePath, int maxSessionHistoryFiles)
        {
            SessionHistoryRefreshResult refreshResult;

            try
            {
                var historyResult = await Task
                    .Run(() => _queryService.GetSessions(workspacePath, maxSessionHistoryFiles))
                    .ConfigureAwait(false);

                refreshResult = new SessionHistoryRefreshResult(workspacePath, historyResult, null);
            }
            catch (Exception ex)
            {
                refreshResult = new SessionHistoryRefreshResult(workspacePath, null, ex.Message);
            }

            CompleteRefresh(workspacePath, refreshResult);
        }

        private void CompleteRefresh(
            string? refreshWorkspacePath,
            SessionHistoryRefreshResult refreshResult)
        {
            var shouldNotify = false;
            var shouldStartNextRefresh = false;
            string? nextRefreshWorkspacePath = null;
            var nextMaxSessionHistoryFiles = 0;

            lock (_lock)
            {
                if (_state == RefreshState.Disposed)
                {
                    return;
                }

                shouldNotify = string.Equals(
                    _workspacePath,
                    refreshWorkspacePath,
                    StringComparison.Ordinal);

                if (string.IsNullOrWhiteSpace(_workspacePath))
                {
                    _state = RefreshState.Idle;
                }
                else if (shouldNotify)
                {
                    _state = RefreshState.Scheduled;
                    _refreshTimer.Change(_refreshInterval, Timeout.InfiniteTimeSpan);
                }
                else
                {
                    _state = RefreshState.Refreshing;
                    nextRefreshWorkspacePath = _workspacePath;
                    nextMaxSessionHistoryFiles = _maxSessionHistoryFiles;
                    shouldStartNextRefresh = true;
                }
            }

            if (shouldNotify)
            {
                Dispatcher.UIThread.Post(() => RefreshCompleted?.Invoke(refreshResult));
            }

            if (shouldStartNextRefresh)
            {
                _ = RefreshAsync(nextRefreshWorkspacePath!, nextMaxSessionHistoryFiles);
            }
        }
    }
}
