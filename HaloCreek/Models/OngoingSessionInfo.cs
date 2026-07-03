using System;

namespace HaloCreek.Models
{
    public sealed record OngoingSessionInfo(
        string Id,
        string Title,
        string WorkspacePath,
        DateTimeOffset StartedAt,
        FrontSessionState FrontState,
        TmuxHeartbeatState HeartbeatState,
        bool IsInteractive)
    {
        public string StatusText => $"{FrontState} / {HeartbeatState}";
    }

    public enum FrontSessionState
    {
        Front,
        Background
    }

    public enum TmuxHeartbeatState
    {
        Idle,
        Active,
    }
}
