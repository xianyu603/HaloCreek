using System;
using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services.SessionHistory
{
    public sealed class SessionHistoryStore : IDisposable
    {
        private readonly SessionHistoryRefresher _refresher;
        private bool _isDisposed;

        public SessionHistoryStore(SessionHistoryQueryService queryService)
        {
            _refresher = new SessionHistoryRefresher(queryService);
            _refresher.RefreshCompleted += ApplyRefreshResult;
        }

        public SessionHistoryStore(ISessionHistoryReader reader)
            : this(new SessionHistoryQueryService(reader))
        {
        }

        public event EventHandler? HistoryChanged;

        public string WorkspacePath { get; private set; } = string.Empty;

        public IReadOnlyList<HistorySessionInfo> Sessions { get; private set; } =
            Array.Empty<HistorySessionInfo>();

        public int SkippedFileCount { get; private set; }

        public string? LastErrorMessage { get; private set; }

        public void StartRefresh()
        {
            _refresher.StartRefresh();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _refresher.RefreshCompleted -= ApplyRefreshResult;
            _refresher.Dispose();
        }

        private void ApplyRefreshResult(SessionHistoryRefreshResult refreshResult)
        {
            ArgumentNullException.ThrowIfNull(refreshResult);

            WorkspacePath = refreshResult.WorkspacePath;
            LastErrorMessage = refreshResult.ErrorMessage;

            if (refreshResult.HistoryResult is not null)
            {
                Sessions = refreshResult.HistoryResult.Sessions;
                SkippedFileCount = refreshResult.HistoryResult.SkippedFileCount;
                LastErrorMessage = null;
            }

            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
