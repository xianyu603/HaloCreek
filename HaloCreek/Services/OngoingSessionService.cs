using System;
using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class OngoingSessionService
    {
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
}
