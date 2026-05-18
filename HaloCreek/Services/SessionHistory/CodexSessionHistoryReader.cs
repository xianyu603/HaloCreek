using System;
using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services.SessionHistory
{
    public sealed class CodexSessionHistoryReader : ISessionHistoryReader
    {
        public IReadOnlyList<HistorySessionInfo> ReadSessions(WorkspaceInfo? workspace, AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            return Array.Empty<HistorySessionInfo>();
        }
    }
}
