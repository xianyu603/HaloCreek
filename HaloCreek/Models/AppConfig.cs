using System;
using System.Collections.Generic;
using System.Linq;

namespace HaloCreek.Models
{
    public sealed record AppConfig(
        string CodexExecutableName,
        IReadOnlyList<string> CodexLaunchArguments,
        int MaxSessionHistoryFiles,
        string DiffToolPath,
        string GitFileBrowserDoubleClickActionId,
        IReadOnlyList<GitFileBrowserActionConfig> GitFileBrowserActions)
    {
        public static AppConfig DefaultForMvp1 { get; } = new(
            "codex",
            Array.Empty<string>(),
            100,
            string.Empty,
            "Open",
            new[]
            {
                new GitFileBrowserActionConfig(
                    "OpenDiff",
                    "OpenDiff",
                    true,
                    GitFileBrowserActionPlacement.Left,
                    "TortoiseGitProc.exe",
                    new[] { "/command:diff", "/path:{SelectedPath}" }),
                new GitFileBrowserActionConfig(
                    "Open",
                    "Open",
                    false,
                    GitFileBrowserActionPlacement.Left,
                    "explorer.exe",
                    new[] { "{SelectedPath}" }),
                new GitFileBrowserActionConfig(
                    "ExploreTo",
                    "ExploreTo",
                    true,
                    GitFileBrowserActionPlacement.Left,
                    "explorer.exe",
                    new[] { "/select,{SelectedPath}" }),
                new GitFileBrowserActionConfig(
                    "Edit",
                    "Edit",
                    true,
                    GitFileBrowserActionPlacement.Left,
                    "notepad.exe",
                    new[] { "{SelectedPath}" }),
                new GitFileBrowserActionConfig(
                    "Commit",
                    "Commit",
                    true,
                    GitFileBrowserActionPlacement.Right,
                    "TortoiseGitProc.exe",
                    new[] { "/command:commit", "/path:{WorkspaceRoot}" }),
                new GitFileBrowserActionConfig(
                    "ShowLog",
                    "Show Log",
                    true,
                    GitFileBrowserActionPlacement.Right,
                    "TortoiseGitProc.exe",
                    new[] { "/command:log", "/path:{WorkspaceRoot}" }),
            });
    }

    public sealed record GitFileBrowserActionConfig(
        string Id,
        string Label,
        bool ShowAsButton,
        GitFileBrowserActionPlacement Placement,
        string Executable,
        IReadOnlyList<string> Arguments)
    {
        public bool RequiresSelectedChange => (Arguments ?? Array.Empty<string>()).Any(UsesSelectedToken);

        private static bool UsesSelectedToken(string argument)
        {
            return argument?.Contains("{SelectedPath}", StringComparison.Ordinal) == true;
        }
    }

    public enum GitFileBrowserActionPlacement
    {
        Left,
        Right
    }
}
