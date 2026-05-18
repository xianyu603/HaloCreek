using System;

namespace HaloCreek.Models
{
    public sealed record HistorySessionInfo(
        string Id,
        string Title,
        string WorkspacePath,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastUpdatedAt,
        HistorySessionState State,
        string InitialPromptPreview);

    public enum HistorySessionState
    {
        Unknown,
        Ready,
        Running,
        Completed,
        Failed
    }
}
