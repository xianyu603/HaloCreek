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
        public IReadOnlyList<GitSelectedPathActionDescriptor> GetGitSelectedPathActions()
        {
            return GitFileBrowserActionDefaults.Actions
                .Where(action => action.Target == GitFileBrowserActionTarget.SelectedFilePath)
                .Select(action => new GitSelectedPathActionDescriptor(action.Id, action.Title))
                .ToArray();
        }

        public IReadOnlyList<GitWorkspaceRootActionDescriptor> GetGitWorkspaceRootActions()
        {
            return GitFileBrowserActionDefaults.Actions
                .Where(action => action.Target == GitFileBrowserActionTarget.WorkspaceRoot)
                .Select(action => new GitWorkspaceRootActionDescriptor(action.Id, action.Title))
                .ToArray();
        }

        public GitSelectedPathActionDescriptor? GetGitSelectedPathDoubleClickAction()
        {
            var action = FindGitFileBrowserAction(GitFileBrowserActionDefaults.DoubleClickActionId);
            return action?.Target == GitFileBrowserActionTarget.SelectedFilePath
                ? new GitSelectedPathActionDescriptor(action.Id, action.Title)
                : null;
        }

        public bool CanRunGitSelectedPathAction(string actionId, string? selectedRelativePath)
        {
            return !string.IsNullOrWhiteSpace(selectedRelativePath);
        }

        public void RunGitSelectedPathAction(string actionId, string selectedRelativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(selectedRelativePath);

            var action = RequireGitFileBrowserAction(actionId);
            if (action.Target != GitFileBrowserActionTarget.SelectedFilePath)
            {
                throw new InvalidOperationException(
                    $"{GetActionName(action)} configuration is invalid: Target must be SelectedFilePath.");
            }

            RunGitFileBrowserAction(action, selectedRelativePath);
        }

        public bool CanRunGitWorkspaceRootAction(string actionId)
        {
            return true;
        }

        public void RunGitWorkspaceRootAction(string actionId)
        {
            var action = RequireGitFileBrowserAction(actionId);
            if (action.Target != GitFileBrowserActionTarget.WorkspaceRoot)
            {
                throw new InvalidOperationException(
                    $"{GetActionName(action)} configuration is invalid: Target must be WorkspaceRoot.");
            }

            RunGitFileBrowserAction(action, null);
        }

        public void OpenDiff(string leftPath, string rightPath, string title)
        {
            try
            {
                StartExternalAction(
                    "Diff",
                    $"Starting external diff. Title={QuoteForLog(title)} "
                    + $"Left={QuoteForLog(leftPath)} Right={QuoteForLog(rightPath)}",
                    $"Started external diff. Title={QuoteForLog(title)}",
                    "TortoiseGitProc.exe",
                    new[] { "/command:diff", $"/path:{rightPath}", $"/path2:{leftPath}" },
                    workingDirectory: null,
                    createNoWindow: false);
            }
            catch (Exception ex) when (IsExternalActionStartException(ex))
            {
                Log.Error("Diff", ex, $"Failed to start external diff. Title={QuoteForLog(title)}");
                throw new InvalidOperationException($"Failed to start {title}: {ex.Message}", ex);
            }
        }

        private static void RunGitFileBrowserAction(
            GitFileBrowserActionExecutionConfig action,
            string? selectedRelativePath)
        {
            var actionName = GetActionName(action);
            var workspacePath = WorkspaceRuntime.Current.GitRootPath;

            if (string.IsNullOrWhiteSpace(action.Executable))
            {
                throw new InvalidOperationException($"Executable is empty for {actionName}.");
            }

            var arguments = action.Arguments ?? Array.Empty<string>();
            if (arguments.Any(argument => argument is null))
            {
                throw new InvalidOperationException($"Argument is null for {actionName}.");
            }

            var validationError = ValidateGitFileBrowserAction(action, selectedRelativePath);
            if (validationError is not null)
            {
                throw new InvalidOperationException(
                    $"{actionName} configuration is invalid: {validationError}");
            }

            try
            {
                var executable = action.Executable.Trim();
                var selectedPath = ResolveSelectedPath(workspacePath, selectedRelativePath);
                var resolvedArguments = arguments
                    .Select(argument => ReplaceActionTokens(argument, workspacePath, selectedPath))
                    .ToArray();

                StartExternalAction(
                    "Git",
                    $"Starting configured action. Action={QuoteForLog(actionName)} "
                    + $"Executable={QuoteForLog(executable)} "
                    + $"WorkingDirectory={QuoteForLog(workspacePath)} "
                    + $"SelectedPath={QuoteForLog(selectedPath)} "
                    + $"Arguments={FormatActionArgumentsForLog(arguments, resolvedArguments)}",
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

        private static GitFileBrowserActionExecutionConfig? FindGitFileBrowserAction(string actionId)
        {
            return GitFileBrowserActionDefaults.Actions.FirstOrDefault(
                action => string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase));
        }

        private static GitFileBrowserActionExecutionConfig RequireGitFileBrowserAction(string actionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

            return FindGitFileBrowserAction(actionId)
                ?? throw new InvalidOperationException($"Git file browser action not found: {actionId}");
        }

        private static string? ValidateGitFileBrowserAction(
            GitFileBrowserActionExecutionConfig action,
            string? selectedRelativePath)
        {
            return action.Target switch
            {
                GitFileBrowserActionTarget.SelectedFilePath when !action.UsesSelectedPathToken =>
                    "SelectedFilePath actions must include the {SelectedPath} token.",
                GitFileBrowserActionTarget.SelectedFilePath when string.IsNullOrWhiteSpace(selectedRelativePath) =>
                    "SelectedFilePath actions require a selected Git change.",
                GitFileBrowserActionTarget.SelectedFilePath => null,
                GitFileBrowserActionTarget.WorkspaceRoot when action.UsesSelectedPathToken =>
                    "WorkspaceRoot actions cannot include the {SelectedPath} token.",
                GitFileBrowserActionTarget.WorkspaceRoot => null,
                _ => "Target must be SelectedFilePath or WorkspaceRoot.",
            };
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

        private static string GetActionName(GitFileBrowserActionExecutionConfig action)
        {
            if (!string.IsNullOrWhiteSpace(action.Id))
            {
                return action.Id.Trim();
            }

            if (!string.IsNullOrWhiteSpace(action.Title))
            {
                return action.Title.Trim();
            }

            return "configured action";
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
    }

    public sealed record GitSelectedPathActionDescriptor(
        string Id,
        string Title);

    public sealed record GitWorkspaceRootActionDescriptor(
        string Id,
        string Title);

    internal sealed record GitFileBrowserActionExecutionConfig(
        string Id,
        string Title,
        GitFileBrowserActionTarget Target,
        string Executable,
        IReadOnlyList<string> Arguments)
    {
        public bool UsesSelectedPathToken => (Arguments ?? Array.Empty<string>()).Any(UsesSelectedPathTokenInArgument);

        private static bool UsesSelectedPathTokenInArgument(string argument)
        {
            return argument?.Contains("{SelectedPath}", StringComparison.Ordinal) == true;
        }
    }

    internal enum GitFileBrowserActionTarget
    {
        SelectedFilePath = 1,
        WorkspaceRoot = 2
    }

    internal static class GitFileBrowserActionDefaults
    {
        public const string DoubleClickActionId = "Open";

        public static IReadOnlyList<GitFileBrowserActionExecutionConfig> Actions { get; } =
            new[]
            {
                new GitFileBrowserActionExecutionConfig(
                    "OpenDiff",
                    "OpenDiff",
                    GitFileBrowserActionTarget.SelectedFilePath,
                    "TortoiseGitProc.exe",
                    new[] { "/command:diff", "/path:{SelectedPath}" }),
                new GitFileBrowserActionExecutionConfig(
                    "Open",
                    "Open",
                    GitFileBrowserActionTarget.SelectedFilePath,
                    "explorer.exe",
                    new[] { "{SelectedPath}" }),
                new GitFileBrowserActionExecutionConfig(
                    "ExploreTo",
                    "ExploreTo",
                    GitFileBrowserActionTarget.SelectedFilePath,
                    "explorer.exe",
                    new[] { "/select,", "{SelectedPath}" }),
                new GitFileBrowserActionExecutionConfig(
                    "Edit",
                    "Edit",
                    GitFileBrowserActionTarget.SelectedFilePath,
                    "notepad.exe",
                    new[] { "{SelectedPath}" }),
                new GitFileBrowserActionExecutionConfig(
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
                new GitFileBrowserActionExecutionConfig(
                    "Delete",
                    "Delete",
                    GitFileBrowserActionTarget.SelectedFilePath,
                    "cmd.exe",
                    new[] { "/c", "del", "/f", "/q", "{SelectedPath}" }),
                new GitFileBrowserActionExecutionConfig(
                    "Commit",
                    "Commit",
                    GitFileBrowserActionTarget.WorkspaceRoot,
                    "TortoiseGitProc.exe",
                    new[] { "/command:commit", "/path:{WorkspaceRoot}" }),
                new GitFileBrowserActionExecutionConfig(
                    "ShowLog",
                    "Show Log",
                    GitFileBrowserActionTarget.WorkspaceRoot,
                    "TortoiseGitProc.exe",
                    new[] { "/command:log", "/path:{WorkspaceRoot}" }),
            };
    }
}
