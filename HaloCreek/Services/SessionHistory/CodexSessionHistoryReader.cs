using System;
using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services.SessionHistory
{
    public sealed class CodexSessionHistoryReader : ISessionHistoryReader
    {
        public IReadOnlyList<HistorySessionInfo> ReadSessions(string? workspacePath, AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            return Array.Empty<HistorySessionInfo>();
        }
    }
}
