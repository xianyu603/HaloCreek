namespace HaloCreek.Services.SessionHistory
{
    public interface ISessionHistoryReader
    {
        SessionHistoryResult ReadSessions(string? workspacePath, int maxSessionHistoryFiles);
    }
}
