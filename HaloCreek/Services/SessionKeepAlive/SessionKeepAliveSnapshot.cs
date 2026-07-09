using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services.SessionKeepAlive
{
    public sealed record SessionKeepAliveSnapshot(int? ExitCode, SessionKeepAliveStatus Status)
        : IKeyedWorkspaceSnapshot<SessionKeepAliveSnapshot>
    {
        private const string PsmuxExecutableName = "psmux";
        private const int DefaultInputPaneDetectionLineCount = 5;
        private static readonly Regex InputPaneSequencePattern = new(
            "\x1B\\[0;2;48;2;[^m\\r\\n]*m>?",
            RegexOptions.Compiled);

        public static SessionKeepAliveSnapshot CreateEmpty()
        {
            return new SessionKeepAliveSnapshot(null, SessionKeepAliveStatus.Other);
        }

        public static SessionKeepAliveSnapshot ReadSnapshot(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            var result = PlatformInfrastructure.RunProcessWithCapturedOutput(
                PsmuxExecutableName,
                new[] { "capture-pane", "-pe", "-t", key + ":0.0"});

            return new SessionKeepAliveSnapshot(
                result.ExitCode,
                ParseStatus(result.ExitCode, result.Output));
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
            return left.ExitCode == right.ExitCode
                && left.Status == right.Status;
        }

        private static SessionKeepAliveStatus ParseStatus(
            int? exitCode,
            string output)
        {
            if (exitCode == 1)
            {
                return SessionKeepAliveStatus.Dead;
            }

            var lastLines = string.Join(
                '\n',
                (output ?? string.Empty)
                    .Replace("\0", string.Empty, StringComparison.Ordinal)
                    .Split('\n')
                    .TakeLast(DefaultInputPaneDetectionLineCount));

            if (InputPaneSequencePattern.IsMatch(lastLines))
            {
                return SessionKeepAliveStatus.HasInputPane;
            }

            return SessionKeepAliveStatus.Other;
        }
    }

    public enum SessionKeepAliveStatus
    {
        Dead,
        HasInputPane,
        Other,
    }
}
