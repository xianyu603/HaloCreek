using System;
using System.Linq;
namespace HaloCreek.Services.SessionHistory
{
    public sealed class SessionHistoryQueryService
    {
        private readonly ISessionHistoryReader _reader;

        public SessionHistoryQueryService(ISessionHistoryReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public SessionHistoryResult GetSessions(string workspacePath, int maxSessionHistoryFiles)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            var result = _reader.ReadSessions(workspacePath, maxSessionHistoryFiles);

            return result with
            {
                Sessions = result.Sessions
                    .OrderByDescending(session => session.LastUpdatedAt)
                    .ToArray()
            };
        }
    }
}
