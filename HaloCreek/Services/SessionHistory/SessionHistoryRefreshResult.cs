namespace HaloCreek.Services.SessionHistory
{
    public sealed record SessionHistoryRefreshResult(
        string WorkspacePath,
        SessionHistoryResult? HistoryResult,
        string? ErrorMessage);
}
