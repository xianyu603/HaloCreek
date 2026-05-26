using System;
using System.Linq;
using HaloCreek.Services;

namespace HaloCreek.Services.SessionHistory
{
    public sealed class SessionHistoryQueryService
    {
        private readonly ConfigService _configService;
        private readonly ISessionHistoryReader _reader;

        public SessionHistoryQueryService(ISessionHistoryReader reader, ConfigService configService)
        {
            _reader = reader;
            _configService = configService;
        }

        public SessionHistoryResult GetSessions(string? workspacePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            var config = _configService.LoadEffectiveConfig(workspacePath);
            var result = _reader.ReadSessions(workspacePath, config);

            return result with
            {
                Sessions = result.Sessions
                    .OrderByDescending(session => session.LastUpdatedAt)
                    .ToArray()
            };
        }
    }
}
