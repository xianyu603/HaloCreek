using System;
using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class SessionLifecycleService
    {
        public SessionLaunchResult Launch(
            WorkspaceInfo workspace,
            string promptText,
            AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentException.ThrowIfNullOrWhiteSpace(promptText);
            ArgumentNullException.ThrowIfNull(config);

            return new SessionLaunchResult(
                false,
                "Session lifecycle is not connected yet.",
                null);
        }

        public IReadOnlyList<OngoingSessionInfo> GetOngoingSessions(WorkspaceInfo? workspace)
        {
            return Array.Empty<OngoingSessionInfo>();
        }

        public bool TryBringToFront(OngoingSessionInfo session)
        {
            ArgumentNullException.ThrowIfNull(session);

            return false;
        }
    }

    public sealed record SessionLaunchResult(
        bool Started,
        string StatusMessage,
        OngoingSessionInfo? Session);
}
