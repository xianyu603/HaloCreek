using System;
using System.Linq;
using System.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services.SessionKeepAlive
{
    public sealed record SessionKeepAliveSnapshot(SessionKeepAliveStatus Status)
        : IKeyedWorkspaceSnapshot<SessionKeepAliveSnapshot>
    {
        private const string PsmuxExecutableName = "psmux";
        private const int DefaultInputPaneDetectionLineCount = 5;
        private const char Escape = '\x1B';
        private const char PromptMarker = '>';
        private const char AlternativePromptMarker = '\u203A';

        public static TimeSpan SnapshotRefreshInterval => TimeSpan.FromSeconds(3);

        public static TimeSpan SnapshotRefreshJitter => TimeSpan.Zero;

        public static SessionKeepAliveSnapshot CreateEmpty()
        {
            return new SessionKeepAliveSnapshot(SessionKeepAliveStatus.Other);
        }

        public static SessionKeepAliveSnapshot ReadSnapshot(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            var result = PlatformInfrastructure.RunProcessWithCapturedOutput(
                PsmuxExecutableName,
                new[] { "capture-pane", "-pe", "-t", key + ":0.0"});

            return new SessionKeepAliveSnapshot(ParseStatus(result.ExitCode, result.Output));
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
            return left.Status == right.Status;
        }

        private static SessionKeepAliveStatus ParseStatus(
            int? exitCode,
            string output)
        {
            if (exitCode == 1)
            {
                return SessionKeepAliveStatus.Dead;
            }

            var hasPromptPattern = false;
            var hasBackgroundWithoutPromptPattern = false;

            foreach (var line in (output ?? string.Empty)
                .Replace("\0", string.Empty, StringComparison.Ordinal)
                .Split('\n')
                .TakeLast(DefaultInputPaneDetectionLineCount))
            {
                switch (ClassifyInputPanePattern(line))
                {
                    case InputPanePattern.Prompt:
                        hasPromptPattern = true;
                        break;
                    case InputPanePattern.BackgroundWithoutPrompt:
                        hasBackgroundWithoutPromptPattern = true;
                        break;
                }
            }

            if (hasPromptPattern && !hasBackgroundWithoutPromptPattern)
            {
                return SessionKeepAliveStatus.HasInputPane;
            }

            return SessionKeepAliveStatus.Other;
        }

        private static InputPanePattern ClassifyInputPanePattern(string line)
        {
            var hasBackground = false;

            for (var index = 0; index < line.Length; index++)
            {
                if (TryApplySgr(line, ref index, ref hasBackground)
                    || line[index] == '\r')
                {
                    continue;
                }

                if (hasBackground)
                {
                    return line[index] is PromptMarker or AlternativePromptMarker
                        ? InputPanePattern.Prompt
                        : InputPanePattern.BackgroundWithoutPrompt;
                }
            }

            return InputPanePattern.None;
        }

        private static bool TryApplySgr(
            string line,
            ref int index,
            ref bool hasBackground)
        {
            if (line[index] != Escape
                || index + 2 >= line.Length
                || line[index + 1] != '[')
            {
                return false;
            }

            var endIndex = line.IndexOf('m', index + 2);
            if (endIndex < 0)
            {
                return false;
            }

            ApplySgr(line[(index + 2)..endIndex], ref hasBackground);
            index = endIndex;
            return true;
        }

        private static void ApplySgr(
            string sgr,
            ref bool hasBackground)
        {
            if (sgr.Length == 0)
            {
                hasBackground = false;
                return;
            }

            var parts = sgr.Split(';');
            for (var index = 0; index < parts.Length; index++)
            {
                if (!int.TryParse(parts[index], out var value))
                {
                    continue;
                }

                if (value is 0 or 49)
                {
                    hasBackground = false;
                }
                else if (value is >= 40 and <= 47 or >= 100 and <= 107)
                {
                    hasBackground = true;
                }
                else if (value is 38 or 48)
                {
                    hasBackground |= value == 48;
                    index += GetExtendedColorParameterCount(parts, index + 1);
                }
            }
        }

        private static int GetExtendedColorParameterCount(
            string[] parts,
            int modeIndex)
        {
            if (modeIndex >= parts.Length
                || !int.TryParse(parts[modeIndex], out var mode))
            {
                return 0;
            }

            return mode switch
            {
                2 => 4,
                5 => 2,
                _ => 1,
            };
        }

        private enum InputPanePattern
        {
            None,
            Prompt,
            BackgroundWithoutPrompt,
        }
    }

    public enum SessionKeepAliveStatus
    {
        Dead,
        HasInputPane,
        Other,
    }
}
