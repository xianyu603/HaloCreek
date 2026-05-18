using System;
using System.Collections.Generic;
using System.Linq;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.Services.SessionHistory
{
    public sealed class SessionHistoryService
    {
        private readonly ConfigService _configService;
        private readonly ISessionHistoryReader _reader;

        public SessionHistoryService(ISessionHistoryReader reader, ConfigService configService)
        {
            _reader = reader;
            _configService = configService;
        }

        public IReadOnlyList<HistorySessionInfo> GetSessions(WorkspaceInfo? workspace)
        {
            var config = _configService.LoadEffectiveConfig(workspace);

            return _reader
                .ReadSessions(workspace, config)
                .OrderByDescending(session => session.LastUpdatedAt)
                .ToArray();
        }

        public IReadOnlyList<HistorySessionInfo> SearchSessions(WorkspaceInfo? workspace, string? searchText)
        {
            var sessions = GetSessions(workspace);

            if (string.IsNullOrWhiteSpace(searchText))
            {
                return sessions;
            }

            var query = searchText.Trim();

            return sessions
                .Where(session =>
                    Contains(session.Title, query) ||
                    Contains(session.InitialPromptPreview, query) ||
                    Contains(session.WorkspacePath, query))
                .ToArray();
        }

        private static bool Contains(string value, string query)
        {
            return value.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }
}
