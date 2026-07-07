using System;

namespace HaloCreek.Models
{
    public sealed class SessionLifecycleChangedEventArgs : EventArgs
    {
        public SessionLifecycleChangedEventArgs(
            SessionLifecycleChangeKind changeKind,
            OngoingSessionInfo? session,
            OngoingSessionInfo? previousSession = null)
        {
            ChangeKind = changeKind;
            Session = session;
            PreviousSession = previousSession;
        }

        public SessionLifecycleChangeKind ChangeKind { get; }

        public OngoingSessionInfo? Session { get; }

        public OngoingSessionInfo? PreviousSession { get; }
    }

    public enum SessionLifecycleChangeKind
    {
        Added,
        Updated,
        Removed,
        FrontChanged,
        StateSnapshotChanged,
    }
}
