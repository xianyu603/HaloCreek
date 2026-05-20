using System;

namespace HaloCreek.Models
{
    public sealed record HistorySessionInfo(
        string Id,
        string WorkspacePath,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastUpdatedAt,
        string InitialPrompt,
        string LastPrompt,
        string LastReply,
        string SessionFilePath);
}
