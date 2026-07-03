using System;
using HaloCreek.Models;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services
{
    public sealed record TmuxHeartbeatSnapshot(TmuxHeartbeatState State)
        : IKeyedWorkspaceSnapshot<TmuxHeartbeatSnapshot>
    {
        public const string HeartbeatDirectory = "/tmp/halocreek/heartbeats";

        private static readonly TimeSpan BackgroundIdleThreshold = TimeSpan.FromSeconds(4);

        public string? SnapshotListenPath { get; init; }

        public static TmuxHeartbeatSnapshot CreateEmpty()
        {
            return new TmuxHeartbeatSnapshot(TmuxHeartbeatState.Idle);
        }

        public static TmuxHeartbeatSnapshot ReadSnapshot(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            var heartbeatPath = GetHeartbeatPath(key);
            var listenPath = GetReadableHeartbeatPath(heartbeatPath);
            if (!WorkspaceRuntime.PlatformInfrastructure.TryGetWslFileLastWriteTimeUtc(
                    heartbeatPath,
                    out var heartbeatLastWriteTimeUtc))
            {
                return new TmuxHeartbeatSnapshot(TmuxHeartbeatState.Idle)
                {
                    SnapshotListenPath = listenPath,
                };
            }

            var state = DateTimeOffset.UtcNow - heartbeatLastWriteTimeUtc < BackgroundIdleThreshold
                ? TmuxHeartbeatState.Active
                : TmuxHeartbeatState.Idle;
            return new TmuxHeartbeatSnapshot(state)
            {
                SnapshotListenPath = listenPath,
            };
        }

        static TmuxHeartbeatSnapshot IWorkspaceSnapshot<TmuxHeartbeatSnapshot>.ReadSnapshot()
        {
            throw new NotSupportedException(
                "TmuxHeartbeatSnapshot requires a tmux session identifier. Use WorkspaceSnapshotStore.Create<TmuxHeartbeatSnapshot>(identifier).");
        }

        public static bool ContentEquals(
            TmuxHeartbeatSnapshot left,
            TmuxHeartbeatSnapshot right)
        {
            return left.State == right.State;
        }

        public static string GetHeartbeatPath(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            return HeartbeatDirectory + "/" + identifier + ".heartbeat";
        }

        private static string? GetReadableHeartbeatPath(string heartbeatPath)
        {
            return HaloCreek.Infrastructure.PlatformInfrastructure.TryConvertWslPathToReadablePath(
                heartbeatPath,
                out var readablePath)
                ? readablePath
                : null;
        }
    }

}
