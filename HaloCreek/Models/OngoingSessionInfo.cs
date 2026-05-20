using System;

namespace HaloCreek.Models
{
    public sealed record OngoingSessionInfo(
        string Id,
        string Title,
        string WorkspacePath,
        DateTimeOffset StartedAt,
        DateTimeOffset LastActivityAt,
        OngoingSessionState State);

    public enum OngoingSessionState
    {
        Unknown,
        Starting,
        Running,
        WaitingForInput,
        Exited
    }
}
