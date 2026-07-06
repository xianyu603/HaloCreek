using System;
using System.Collections.Generic;
using System.Linq;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services.SessionState
{
    public sealed record SessionStateSnapshot(
        SessionTaskState State,
        DateTimeOffset? StateTimestamp,
        DateTimeOffset? LastActiveTime,
        bool? Active,
        IReadOnlyList<SessionMessage> Messages,
        SessionTokenInfo? TokenInfo)
        : IKeyedWorkspaceSnapshot<SessionStateSnapshot>
    {
        public static readonly TimeSpan ActiveThreshold = TimeSpan.FromSeconds(4);

        public static TimeSpan SnapshotRefreshInterval => TimeSpan.FromSeconds(1);

        public static TimeSpan SnapshotRefreshJitter => TimeSpan.Zero;

        public string? SnapshotListenPath { get; init; }

        public static SessionStateSnapshot CreateEmpty()
        {
            return new SessionStateSnapshot(
                SessionTaskState.Unknown,
                null,
                null,
                null,
                Array.Empty<SessionMessage>(),
                null);
        }

        public static SessionStateSnapshot ReadSnapshot(string key)
        {
            return CodexSessionStateReader.ReadSessionState(key);
        }

        static SessionStateSnapshot IWorkspaceSnapshot<SessionStateSnapshot>.ReadSnapshot()
        {
            throw new NotSupportedException(
                "SessionStateSnapshot requires a session id. Use WorkspaceSnapshotStore.Create<SessionStateSnapshot>(sessionId).");
        }

        public static bool ContentEquals(
            SessionStateSnapshot left,
            SessionStateSnapshot right)
        {
            return left.State == right.State
                && left.StateTimestamp == right.StateTimestamp
                && left.LastActiveTime == right.LastActiveTime
                && left.Active == right.Active
                && Equals(left.TokenInfo, right.TokenInfo)
                && left.Messages.SequenceEqual(right.Messages);
        }

    }

    public sealed record SessionMessage(
        DateTimeOffset Timestamp,
        SessionMessageSender Sender,
        string Message,
        SessionMessagePhase Phase);

    public sealed record SessionTokenInfo(
        long? ContextWindow,
        long? TotalTokenUsageTotal,
        long? LastTokenUsageTotal);

    public enum SessionTaskState
    {
        Unknown,
        TaskStarted,
        TaskAborted,
        TaskCompleted,
    }

    public enum SessionMessageSender
    {
        User,
        Agent,
    }

    public enum SessionMessagePhase
    {
        Unknown,
        Commentary,
        FinalAnswer,
    }
}
