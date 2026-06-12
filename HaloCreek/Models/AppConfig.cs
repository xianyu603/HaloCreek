using System;
using System.Collections.Generic;
using System.Linq;

namespace HaloCreek.Models
{
    public sealed record AppConfig(
        string CodexExecutableName,
        IReadOnlyList<string> CodexLaunchArguments,
        int MaxSessionHistoryFiles)
    {
        public static AppConfig DefaultForMvp1 { get; } = new(
            "codex",
            Array.Empty<string>(),
            100);
    }

    // TODO ????????????
    public static class GitFileBrowserActionDefaults
    {
        public const string DoubleClickActionId = "Open";

        public static IReadOnlyList<GitFileBrowserActionConfig> Actions { get; } =
            new[]
            {
                new GitFileBrowserActionConfig(
                    "OpenDiff",
                    "OpenDiff",
                    GitFileBrowserActionTarget.SelectedFilePath,
                    "TortoiseGitProc.exe",
                    new[] { "/command:diff", "/path:{SelectedPath}" }),
                new GitFileBrowserActionConfig(
                    "Open",
                    "Open",
                    GitFileBrowserActionTarget.SelectedFilePath,
                    "explorer.exe",
                    new[] { "{SelectedPath}" }),
                new GitFileBrowserActionConfig(
                    "ExploreTo",
                    "ExploreTo",
                    GitFileBrowserActionTarget.SelectedFilePath,
                    "explorer.exe",
                    new[] { "/select,{SelectedPath}" }),
                new GitFileBrowserActionConfig(
                    "Edit",
                    "Edit",
                    GitFileBrowserActionTarget.SelectedFilePath,
                    "notepad.exe",
                    new[] { "{SelectedPath}" }),
                new GitFileBrowserActionConfig(
                    "Revert",
                    "Revert",
                    GitFileBrowserActionTarget.SelectedFilePath,
                    "git",
                    new[]
                    {
                        "restore",
                        "--source=HEAD",
                        "--staged",
                        "--worktree",
                        "--",
                        "{SelectedPath}",
                    }),
                new GitFileBrowserActionConfig(
                    "Delete",
                    "Delete",
                    GitFileBrowserActionTarget.SelectedFilePath,
                    "cmd.exe",
                    new[] { "/c", "del", "/f", "/q", "{SelectedPath}" }),
                new GitFileBrowserActionConfig(
                    "Commit",
                    "Commit",
                    GitFileBrowserActionTarget.WorkspaceRoot,
                    "TortoiseGitProc.exe",
                    new[] { "/command:commit", "/path:{WorkspaceRoot}" }),
                new GitFileBrowserActionConfig(
                    "ShowLog",
                    "Show Log",
                    GitFileBrowserActionTarget.WorkspaceRoot,
                    "TortoiseGitProc.exe",
                    new[] { "/command:log", "/path:{WorkspaceRoot}" }),
            };
    }

    public sealed record GitFileBrowserActionConfig(
        string Id,
        string Label,
        GitFileBrowserActionTarget Target,
        string Executable,
        IReadOnlyList<string> Arguments)
    {
        public bool RequiresSelectedChange => Target == GitFileBrowserActionTarget.SelectedFilePath;

        public bool UsesSelectedPathToken => (Arguments ?? Array.Empty<string>()).Any(UsesSelectedPathTokenInArgument);

        private static bool UsesSelectedPathTokenInArgument(string argument)
        {
            return argument?.Contains("{SelectedPath}", StringComparison.Ordinal) == true;
        }
    }

    public enum GitFileBrowserActionTarget
    {
        SelectedFilePath = 1,
        WorkspaceRoot = 2
    }
}
