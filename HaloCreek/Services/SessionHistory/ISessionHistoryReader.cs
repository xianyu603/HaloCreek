using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services.SessionHistory
{
    public interface ISessionHistoryReader
    {
        IReadOnlyList<HistorySessionInfo> ReadSessions(string? workspacePath, AppConfig config);
    }
}
