using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HaloCreek.Services;

namespace HaloCreek.Services.SessionHistory
{
    // 因为占用了系统资源(timer) 所以要写IDisposable
    public sealed class SessionHistoryRefreshService : IDisposable
    {
        private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(10);

        private readonly SessionHistoryQueryService _queryService;
        private readonly TimeSpan _refreshInterval;
        private readonly Timer _refreshTimer;
        private readonly object _lock = new();
        private Action<SessionHistoryRefreshResult>? _refreshCompleted;
        private string? _workspacePath;
        private bool _isRefreshRunning;
        private bool _isDisposed;

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

        public SessionHistoryRefreshService(ISessionHistoryReader reader, ConfigService configService)
            : this(new SessionHistoryQueryService(reader, configService))
        {
        }

        public void SetWorkspacePath(string? workspacePath)
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);// 契约 还想让我setworkspace不应该在那之前销毁我

                _workspacePath = workspacePath;

                if (string.IsNullOrWhiteSpace(workspacePath))
                {
                    _refreshTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    return;
                }

                _refreshTimer.Change(_refreshInterval, _refreshInterval);
            }
            _ = RefreshAsync();
        }

        public void SetRefreshCompletedHandler(Action<SessionHistoryRefreshResult> refreshCompleted)
        {
            _refreshCompleted = refreshCompleted ?? throw new ArgumentNullException(nameof(refreshCompleted));
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _refreshTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            _refreshTimer.Dispose();
        }

        private void OnRefreshTimerTick(object? state)
        {
            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            string? workspacePath;
            lock (_lock)
            {
                if (_isDisposed || _isRefreshRunning)
                {
                    // TODO 这里应当加日志
                    return;
                }

                if (string.IsNullOrWhiteSpace(_workspacePath))
                {
                    // 为空不刷新
                    return;
                }

                _isRefreshRunning = true;
                workspacePath = _workspacePath;
            }

            SessionHistoryRefreshResult refreshResult;

            try
            {
                var historyResult = await Task
                    .Run(() => _queryService.GetSessions(workspacePath))
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

            lock (_lock)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isRefreshRunning = false;
                shouldNotify = string.Equals(
                    _workspacePath,
                    refreshWorkspacePath,
                    StringComparison.Ordinal);
            }

            if (shouldNotify)
            {
                var refreshCompleted = _refreshCompleted;
                Dispatcher.UIThread.Post(() => refreshCompleted?.Invoke(refreshResult));
            }
        }
    }
}
