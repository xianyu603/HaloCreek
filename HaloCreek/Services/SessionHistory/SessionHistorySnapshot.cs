using System;
using System.Collections.Generic;
using System.Linq;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services.SessionHistory
{
    public sealed record SessionHistorySnapshot(
        string WorkspacePath,
        IReadOnlyList<HistorySessionInfo> Sessions,
        int SkippedFileCount)
        : IWorkspaceSnapshot<SessionHistorySnapshot>
    {
        public static SessionHistorySnapshot CreateEmpty(WorkspaceContext workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            return new SessionHistorySnapshot(
                workspace.WorkspacePath,
                Array.Empty<HistorySessionInfo>(),
                0);
        }

        public static SessionHistorySnapshot ReadSnapshot(WorkspaceContext workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            var result = CodexSessionHistoryReader.ReadSessions(
                workspace.WorkspacePath,
                workspace.EffectiveConfig.MaxSessionHistoryFiles);

            return new SessionHistorySnapshot(
                workspace.WorkspacePath,
                result.Sessions
                    .OrderByDescending(session => session.LastUpdatedAt)
                    .ToArray(),
                result.SkippedFileCount);
        }

        public static bool ContentEquals(
            SessionHistorySnapshot left,
            SessionHistorySnapshot right)
        {
            return string.Equals(left.WorkspacePath, right.WorkspacePath, StringComparison.Ordinal)
                && left.SkippedFileCount == right.SkippedFileCount
                && left.Sessions.SequenceEqual(right.Sessions);
        }
    }
}
