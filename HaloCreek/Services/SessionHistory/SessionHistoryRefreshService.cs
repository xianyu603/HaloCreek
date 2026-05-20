using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using HaloCreek.Services;

namespace HaloCreek.Services.SessionHistory
{
    public sealed class SessionHistoryRefreshService
    {
        private readonly SessionHistoryQueryService _queryService;
        private Action<SessionHistoryRefreshResult>? _refreshCompleted;
        private string? _workspacePath;

        public SessionHistoryRefreshService(SessionHistoryQueryService queryService)
        {
            _queryService = queryService;
        }

        public SessionHistoryRefreshService(ISessionHistoryReader reader, ConfigService configService)
            : this(new SessionHistoryQueryService(reader, configService))
        {
        }

        public void SetWorkspacePath(string? workspacePath)
        {
            _workspacePath = workspacePath;
        }

        public void SetRefreshCompletedHandler(Action<SessionHistoryRefreshResult> refreshCompleted)
        {
            _refreshCompleted = refreshCompleted ?? throw new ArgumentNullException(nameof(refreshCompleted));
        }

        public void RequestRefresh()
        {
            var workspacePath = _workspacePath;
            _ = RefreshAsync(workspacePath);
        }

        private async Task RefreshAsync(string? workspacePath)
        {
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

            Dispatcher.UIThread.Post(() => _refreshCompleted?.Invoke(refreshResult));
        }
    }
}
