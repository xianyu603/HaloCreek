using System.Linq;
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

        public SessionHistoryResult GetSessions(string? workspacePath)
        {
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
