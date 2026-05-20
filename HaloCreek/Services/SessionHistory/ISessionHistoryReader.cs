using HaloCreek.Models;

namespace HaloCreek.Services.SessionHistory
{
    public interface ISessionHistoryReader
    {
        SessionHistoryResult ReadSessions(string? workspacePath, AppConfig config);
    }
}
