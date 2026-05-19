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

        public IReadOnlyList<HistorySessionInfo> GetSessions(string? workspacePath)
        {
            var config = _configService.LoadEffectiveConfig(workspacePath);

            return _reader
                .ReadSessions(workspacePath, config)
                .OrderByDescending(session => session.LastUpdatedAt)
                .ToArray();
        }

        public IReadOnlyList<HistorySessionInfo> SearchSessions(string? workspacePath, string? searchText)
        {
            var sessions = GetSessions(workspacePath);

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
