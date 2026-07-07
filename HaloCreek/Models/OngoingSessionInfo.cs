using System;
using HaloCreek.Services.SessionState;

namespace HaloCreek.Models
{
    public sealed record OngoingSessionInfo(
        string Id,
        string Title,
        string WorkspacePath,
        DateTimeOffset StartedAt,
        FrontSessionState FrontState,
        SessionStateSnapshot StateSnapshot,
        bool IsInteractive)
    {
        public string StatusText => $"{FrontState} / {FormatActivity()}";

        private string FormatActivity()
        {
            return StateSnapshot.Active switch
            {
                true => "Active",
                false => "Idle",
                _ => "Unknown",
            };
        }
    }

    public enum FrontSessionState
    {
        Front,
        Background
    }
}
