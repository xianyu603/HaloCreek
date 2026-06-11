using System;

namespace HaloCreek.Models
{
    public sealed record OngoingSessionInfo(
        string Id,
        string Title,
        string WorkspacePath,
        DateTimeOffset StartedAt,
        OngoingSessionState State);

    public enum OngoingSessionState
    {
        Launching,
        Front,
        BackgroundRunning,
        BackgroundIdle,
        Unknown
    }
}
