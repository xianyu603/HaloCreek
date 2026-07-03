using System;
using System.Collections.Generic;
using System.Linq;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services.SessionHistory
{
    public sealed record SessionHistorySnapshot(
        IReadOnlyList<HistorySessionInfo> Sessions,
        int SkippedFileCount)
        : IWorkspaceSnapshot<SessionHistorySnapshot>
    {
        public string? SnapshotListenPath { get; init; }

        public static SessionHistorySnapshot CreateEmpty()
        {
            return new SessionHistorySnapshot(
                Array.Empty<HistorySessionInfo>(),
                0);
        }

        public static SessionHistorySnapshot ReadSnapshot()
        {
            var workspace = WorkspaceRuntime.Current;
            var result = CodexSessionHistoryReader.ReadSessions(
                workspace.WorkspacePath,
                workspace.EffectiveConfig.MaxSessionHistoryFiles);

            return new SessionHistorySnapshot(
                result.Sessions
                    .OrderByDescending(session => session.LastUpdatedAt)
                    .ToArray(),
                result.SkippedFileCount)
            {
                // WSL file watching is temporarily disabled. Timer polling still refreshes history.
                // SnapshotListenPath = result.SessionHistoryRootPath,
            };
        }

        public static bool ContentEquals(
            SessionHistorySnapshot left,
            SessionHistorySnapshot right)
        {
            return left.SkippedFileCount == right.SkippedFileCount && left.Sessions.SequenceEqual(right.Sessions);
        }
    }
}
