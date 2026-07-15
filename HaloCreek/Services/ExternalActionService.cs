using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;

namespace HaloCreek.Services
{
    public sealed class ExternalActionService
    {
        internal const string TortoiseGitProcExecutableName = "TortoiseGitProc.exe";
        private const string VsCodeExecutableName = "code";
        private const string GitExecutableName = "git";

        private readonly DiffToolKind _diffToolKind;
        private readonly IReadOnlyList<GitFileBrowserAction> _selectedPathActions;
        private readonly IReadOnlyList<GitFileBrowserAction> _workspaceRootActions;

        public ExternalActionService()
        {
            _diffToolKind = ResolveDiffToolKind();
            _selectedPathActions = ResolveSelectedPathActions();
            _workspaceRootActions = GitFileBrowserActions.ResolveWorkspaceRootActions();
        }

        public IReadOnlyList<GitSelectedPathActionDescriptor> GetGitSelectedPathActions()
        {
            return _selectedPathActions
                .Select(action => new GitSelectedPathActionDescriptor(action.Id, action.Title))
                .ToArray();
        }

        public IReadOnlyList<GitWorkspaceRootActionDescriptor> GetGitWorkspaceRootActions()
        {
            return _workspaceRootActions
                .Select(action => new GitWorkspaceRootActionDescriptor(action.Id, action.Title))
                .ToArray();
        }

        public GitSelectedPathActionDescriptor GetGitSelectedPathDoubleClickAction()
        {
            var action = GitFileBrowserActions.SelectedPathDoubleClickAction;
            return new GitSelectedPathActionDescriptor(action.Id, action.Title);
        }

        public void RunGitSelectedPathAction(string actionId, string selectedRelativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(selectedRelativePath);

            var action = RequireGitSelectedPathAction(actionId);
            RunGitFileBrowserAction(action, selectedRelativePath);
        }

        public void RunGitWorkspaceRootAction(string actionId)
        {
            var action = RequireGitWorkspaceRootAction(actionId);
            RunGitFileBrowserAction(action, null);
        }

        public void OpenDiff(string leftPath, string rightPath, string title)
        {
            if (_diffToolKind == DiffToolKind.None)
            {
                throw new InvalidOperationException(
                    $"Cannot start {title}: no supported external diff tool was found.");
            }

            try
            {
                var (executable, arguments) = _diffToolKind switch
                {
                    DiffToolKind.TortoiseGit => (
                        TortoiseGitProcExecutableName,
                        new[] { "/command", "diff", "/path", rightPath, "/path2", leftPath }),
                    DiffToolKind.VsCode => (
                        "cmd.exe",
                        new[] { "/c", VsCodeExecutableName, "--diff", leftPath, rightPath }),
                    _ => throw new InvalidOperationException($"Unsupported diff tool kind: {_diffToolKind}"),
                };

                StartExternalAction(
                    "Diff",
                    $"Starting external diff. Title={QuoteForLog(title)} "
                    + $"Left={QuoteForLog(leftPath)} Right={QuoteForLog(rightPath)}",
                    $"Started external diff. Title={QuoteForLog(title)}",
                    executable,
                    arguments,
                    workingDirectory: null,
                    createNoWindow: true);
            }
            catch (Exception ex) when (IsExternalActionStartException(ex))
            {
                Log.Error("Diff", ex, $"Failed to start external diff. Title={QuoteForLog(title)}");
                throw new InvalidOperationException($"Failed to start {title}: {ex.Message}", ex);
            }
        }

        private static void RunGitFileBrowserAction(
            GitFileBrowserAction action,
            string? selectedRelativePath)
        {
            var actionName = action.Id;
            var workspacePath = WorkspaceRuntime.Current.WorkspacePath;

            try
            {
                var executable = action.Executable.Trim();
                var selectedPath = ResolveSelectedPath(workspacePath, selectedRelativePath);
                var resolvedArguments = action.Arguments
                    .Select(argument => ReplaceActionTokens(argument, workspacePath, selectedPath))
                    .ToArray();

                StartExternalAction(
                    "Git",
                    $"Starting configured action. Action={QuoteForLog(actionName)} "
                    + $"Executable={QuoteForLog(executable)} "
                    + $"WorkingDirectory={QuoteForLog(workspacePath)} "
                    + $"SelectedPath={QuoteForLog(selectedPath)} "
                    + $"Arguments={FormatActionArgumentsForLog(action.Arguments, resolvedArguments)}",
                    $"Started configured action. Action={QuoteForLog(actionName)} "
                    + $"Executable={QuoteForLog(executable)}",
                    executable,
                    resolvedArguments,
                    workingDirectory: workspacePath,
                    createNoWindow: true);
            }
            catch (Exception ex) when (IsExternalActionStartException(ex))
            {
                Log.Error("Git", ex, $"Failed to start configured action. Action={QuoteForLog(actionName)}");
                throw new InvalidOperationException($"Failed to start {actionName}: {ex.Message}", ex);
            }
        }

        private static void StartExternalAction(
            string logCategory,
            string startingMessage,
            string startedMessagePrefix,
            string executable,
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            bool createNoWindow)
        {
            Log.Info(logCategory, startingMessage);

            var processId = PlatformInfrastructure.StartProcess(
                executable,
                arguments,
                workingDirectory,
                createNoWindow);

            Log.Info(
                logCategory,
                $"{startedMessagePrefix} ProcessId={processId}");
        }

        private GitFileBrowserAction RequireGitSelectedPathAction(string actionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

            return _selectedPathActions.FirstOrDefault(
                    action => string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Git selected path action not found: {actionId}");
        }

        private GitFileBrowserAction RequireGitWorkspaceRootAction(string actionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

            return _workspaceRootActions.FirstOrDefault(
                    action => string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Git workspace root action not found: {actionId}");
        }

        private static string ReplaceActionTokens(
            string argument,
            string workspacePath,
            string selectedPath)
        {
            return argument
                .Replace(
                    "{WorkspaceRoot}",
                    PlatformInfrastructure.NormalizePathForCurrentPlatform(workspacePath),
                    StringComparison.Ordinal)
                .Replace(
                    "{SelectedPath}",
                    selectedPath,
                    StringComparison.Ordinal);
        }

        private static string ResolveSelectedPath(string workspacePath, string? selectedRelativePath)
        {
            return string.IsNullOrWhiteSpace(selectedRelativePath)
                ? string.Empty
                : PlatformInfrastructure.CombinePathForCurrentPlatform(workspacePath, selectedRelativePath);
        }

        private static string FormatActionArgumentsForLog(
            IReadOnlyList<string> originalArguments,
            IReadOnlyList<string> resolvedArguments)
        {
            return string.Join(
                ", ",
                originalArguments.Select((argument, index) =>
                    $"[{index}]Raw={QuoteForLog(argument)} Resolved={QuoteForLog(resolvedArguments[index])}"));
        }

        private static string QuoteForLog(string? value)
        {
            return value is null
                ? "<null>"
                : $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        private static bool IsExternalActionStartException(Exception ex)
        {
            return ex is Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException;
        }

        private static DiffToolKind ResolveDiffToolKind()
        {
            if (PlatformInfrastructure.IsExecutableOnPath(TortoiseGitProcExecutableName))
            {
                Log.Info("Diff", "Diff actions use TortoiseGit.");
                return DiffToolKind.TortoiseGit;
            }

            if (PlatformInfrastructure.IsExecutableOnPath(VsCodeExecutableName))
            {
                Log.Info("Diff", "Diff actions use VS Code.");
                return DiffToolKind.VsCode;
            }

            // TODO: Add a user-configurable diff tool path instead of hardcoding supported probes.
            Log.Info("Diff", "No supported external diff tool found. Optional Git file diff action is disabled.");
            return DiffToolKind.None;
        }

        private IReadOnlyList<GitFileBrowserAction> ResolveSelectedPathActions()
        {
            GitFileBrowserAction? openDiffAction = _diffToolKind switch
            {
                DiffToolKind.TortoiseGit => new GitFileBrowserAction(
                    "OpenDiff",
                    "OpenDiff",
                    TortoiseGitProcExecutableName,
                    new[] { "/command", "diff", "/path", "{SelectedPath}" }),
                DiffToolKind.VsCode => new GitFileBrowserAction(
                    "OpenDiff",
                    "OpenDiff",
                    GitExecutableName,
                    new[] { "difftool", "--no-prompt", "--extcmd", "code --diff", "HEAD", "--", "{SelectedPath}" }),
                DiffToolKind.None => null,
                _ => throw new InvalidOperationException($"Unsupported diff tool kind: {_diffToolKind}"),
            };

            var actions = new List<GitFileBrowserAction>();
            if (openDiffAction is not null)
            {
                actions.Add(openDiffAction);
            }

            actions.AddRange(new[]
            {
                GitFileBrowserActions.SelectedPathDoubleClickAction,
                new GitFileBrowserAction(
                    "ExploreTo",
                    "ExploreTo",
                    "explorer.exe",
                    new[] { "/select,", "{SelectedPath}" }),
                new GitFileBrowserAction(
                    "Edit",
                    "Edit",
                    "notepad.exe",
                    new[] { "{SelectedPath}" }),
                new GitFileBrowserAction(
                    "Revert",
                    "Revert",
                    GitExecutableName,
                    new[]
                    {
                        "restore",
                        "--source=HEAD",
                        "--staged",
                        "--worktree",
                        "--",
                        "{SelectedPath}",
                    }),
                new GitFileBrowserAction(
                    "Delete",
                    "Delete",
                    "cmd.exe",
                    new[] { "/c", "del", "/f", "/q", "{SelectedPath}" }),
            });

            return actions;
        }

        private enum DiffToolKind
        {
            TortoiseGit,
            VsCode,
            None,
        }
    }

    public sealed record GitSelectedPathActionDescriptor(
        string Id,
        string Title);

    public sealed record GitWorkspaceRootActionDescriptor(
        string Id,
        string Title);

    internal sealed record GitFileBrowserAction(
        string Id,
        string Title,
        string Executable,
        IReadOnlyList<string> Arguments);

    internal static class GitFileBrowserActions
    {
        private const string SourceTreeExecutableName = "SourceTree.exe";

        public static GitFileBrowserAction SelectedPathDoubleClickAction { get; } = new(
            "Open",
            "Open",
            "explorer.exe",
            new[] { "{SelectedPath}" });

        public static IReadOnlyList<GitFileBrowserAction> ResolveWorkspaceRootActions()
        {
            if (PlatformInfrastructure.IsExecutableOnPath(ExternalActionService.TortoiseGitProcExecutableName))
            {
                Log.Info("Git", "Workspace root actions use TortoiseGit.");
                return GetTortoiseGitWorkspaceRootActions();
            }

            if (PlatformInfrastructure.IsExecutableOnPath(SourceTreeExecutableName))
            {
                Log.Info("Git", "Workspace root actions use SourceTree.");
                return GetSourceTreeWorkspaceRootActions();
            }

            Log.Info("Git", "Workspace root actions use empty external Git tool configuration.");
            return Array.Empty<GitFileBrowserAction>();
        }

        private static IReadOnlyList<GitFileBrowserAction> GetTortoiseGitWorkspaceRootActions()
        {
            return new[]
            {
                new GitFileBrowserAction(
                    "Commit",
                    "Commit",
                    ExternalActionService.TortoiseGitProcExecutableName,
                    new[] { "/command", "commit", "/path", "{WorkspaceRoot}" }),
                new GitFileBrowserAction(
                    "ShowLog",
                    "Show Log",
                    ExternalActionService.TortoiseGitProcExecutableName,
                    new[] { "/command", "log", "/path", "{WorkspaceRoot}" }),
            };
        }

        private static IReadOnlyList<GitFileBrowserAction> GetSourceTreeWorkspaceRootActions()
        {
            return new[]
            {
                new GitFileBrowserAction(
                    "Commit",
                    "Commit",
                    SourceTreeExecutableName,
                    new[] { "-f", "{WorkspaceRoot}", "commit" }),
                new GitFileBrowserAction(
                    "ShowLog",
                    "Show Log",
                    SourceTreeExecutableName,
                    new[] { "-f", "{WorkspaceRoot}", "log" }),
            };
        }
    }
}
