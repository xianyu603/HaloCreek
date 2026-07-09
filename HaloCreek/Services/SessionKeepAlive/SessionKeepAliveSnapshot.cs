using System;
using System.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services.SessionKeepAlive
{
    public sealed record SessionKeepAliveSnapshot(int? ExitCode)
        : IKeyedWorkspaceSnapshot<SessionKeepAliveSnapshot>
    {
        private const string PsmuxExecutableName = "psmux";

        public static SessionKeepAliveSnapshot CreateEmpty()
        {
            return new SessionKeepAliveSnapshot((int?)null);
        }

        public static SessionKeepAliveSnapshot ReadSnapshot(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            var result = PlatformInfrastructure.RunProcessWithCapturedOutput(
                PsmuxExecutableName,
                new[] { "capture-pane", "-p", "-t", key + ":0.0"});

            return new SessionKeepAliveSnapshot(result.ExitCode);
        }

        static SessionKeepAliveSnapshot IWorkspaceSnapshot<SessionKeepAliveSnapshot>.ReadSnapshot()
        {
            throw new NotSupportedException(
                "SessionKeepAliveSnapshot requires a tmux session id. Use WorkspaceSnapshotStore.Create<SessionKeepAliveSnapshot>(sessionId).");
        }

        public static bool ContentEquals(
            SessionKeepAliveSnapshot left,
            SessionKeepAliveSnapshot right)
        {
            return left.ExitCode == right.ExitCode;
        }
    }
}
