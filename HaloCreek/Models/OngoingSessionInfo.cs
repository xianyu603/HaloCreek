using System;
using HaloCreek.Services.SessionState;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Models
{
    public sealed record OngoingSessionInfo(
        string Id,
        string Title,
        string WorkspacePath,
        DateTimeOffset StartedAt,
        FrontSessionState FrontState,
        IWorkspaceSnapshotSource<SessionStateSnapshot>? StateSnapshots,
        bool IsInteractive)
    {
        public string StatusText => $"{FrontState} / {FormatActivity()}";

        private string FormatActivity()
        {
            return StateSnapshots?.Current.Active switch
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
