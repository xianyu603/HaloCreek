using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services.SessionHistory
{
    public interface ISessionHistoryReader
    {
        IReadOnlyList<HistorySessionInfo> ReadSessions(WorkspaceInfo? workspace, AppConfig config);
    }
}
