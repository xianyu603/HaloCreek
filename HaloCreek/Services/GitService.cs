using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class GitService
    {
        private const string GitExecutableName = "git";

        private readonly WorkspaceRuntimeService _workspaceRuntimeService;

        public GitService(WorkspaceRuntimeService workspaceRuntimeService)
        {
            _workspaceRuntimeService = workspaceRuntimeService
                ?? throw new ArgumentNullException(nameof(workspaceRuntimeService));
        }

        public GitChangesResult GetChanges()
        {
            var workspacePath = _workspaceRuntimeService.CurrentWorkspacePath;
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new GitChangesResult(
                    Array.Empty<GitChangeInfo>(),
                    "Select a workspace to view Git changes.");
            }

            var commandResult = RunGitStatus(workspacePath);
            if (!commandResult.Succeeded)
            {
                var message = string.IsNullOrWhiteSpace(commandResult.ErrorMessage)
                    ? "Git status failed."
                    : commandResult.ErrorMessage.Trim();
                if (message.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Current workspace is not a Git repository.";
                }

                return new GitChangesResult(Array.Empty<GitChangeInfo>(), message, workspacePath);
            }

            var changes = ParsePorcelainStatus(commandResult.Output)
                .OrderBy(change => change.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.OriginalRelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var loadedMessage = changes.Length == 0
                ? "No Git changes for current workspace."
                : $"Loaded {changes.Length} Git changes.";

            return new GitChangesResult(changes, loadedMessage, workspacePath);
        }

        public GitOperationResult TryRunConfiguredAction(
            GitChangeInfo? selectedChange,
            GitFileBrowserActionConfig action)
        {
            ArgumentNullException.ThrowIfNull(action);

            var actionName = GetActionName(action);
            var workspacePath = _workspaceRuntimeService.CurrentWorkspacePath;
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new GitOperationResult(false, $"No workspace selected for {actionName}.");
            }

            if (string.IsNullOrWhiteSpace(action.Executable))
            {
                return new GitOperationResult(false, $"Executable is empty for {actionName}.");
            }

            if (action.RequiresSelectedChange && selectedChange is null)
            {
                return new GitOperationResult(false, $"{actionName} requires a selected Git change.");
            }

            var arguments = action.Arguments ?? Array.Empty<string>();
            if (arguments.Any(argument => argument is null))
            {
                return new GitOperationResult(false, $"Argument is null for {actionName}.");
            }

            try
            {
                var executable = action.Executable.Trim();
                var resolvedArguments = arguments
                    .Select(argument => ReplaceActionTokens(
                        argument,
                        workspacePath,
                        selectedChange))
                    .ToArray();

                Log.Info(
                    "Git",
                    $"Starting configured action. Action={QuoteForLog(actionName)} "
                    + $"Executable={QuoteForLog(executable)} "
                    + $"WorkingDirectory={QuoteForLog(workspacePath)} "
                    + $"SelectedPath={QuoteForLog(ResolveSelectedPath(workspacePath, selectedChange))} "
                    + $"Arguments={FormatActionArgumentsForLog(arguments, resolvedArguments)}");

                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = workspacePath,
                    UseShellExecute = false,
                };

                foreach (var argument in resolvedArguments)
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                process.Start();
                Log.Info(
                    "Git",
                    $"Started configured action. Action={QuoteForLog(actionName)} "
                    + $"Executable={QuoteForLog(executable)} "
                    + $"ProcessId={process.Id}");
                return new GitOperationResult(true, $"Started {actionName}.");
            }
            catch (Exception ex) when (ex is Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                Log.Error("Git", ex, $"Failed to start configured action. Action={QuoteForLog(actionName)}");
                return new GitOperationResult(false, $"Failed to start {actionName}: {ex.Message}");
            }
        }

        private static GitCommandResult RunGitStatus(string workspacePath)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = GitExecutableName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                process.StartInfo.ArgumentList.Add("-C");
                process.StartInfo.ArgumentList.Add(workspacePath);
                process.StartInfo.ArgumentList.Add("status");
                process.StartInfo.ArgumentList.Add("--porcelain=v1");
                process.StartInfo.ArgumentList.Add("-z");
                process.StartInfo.ArgumentList.Add("--untracked-files=all");

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                return new GitCommandResult(process.ExitCode == 0, output, error);
            }
            catch (Exception ex) when (ex is Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
            {
                return new GitCommandResult(false, string.Empty, ex.Message);
            }
        }

        private static IReadOnlyList<GitChangeInfo> ParsePorcelainStatus(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return Array.Empty<GitChangeInfo>();
            }

            var tokens = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            var changes = new List<GitChangeInfo>(tokens.Length);

            for (var index = 0; index < tokens.Length; index++)
            {
                var token = tokens[index];
                if (token.Length < 4)
                {
                    continue;
                }

                var indexStatus = token[0];
                var workTreeStatus = token[1];
                var relativePath = token[3..];
                string? originalRelativePath = null;

                if (RequiresOriginalPath(indexStatus, workTreeStatus) && index + 1 < tokens.Length)
                {
                    index++;
                    originalRelativePath = tokens[index];
                }

                changes.Add(new GitChangeInfo(
                    relativePath,
                    ToChangeType(indexStatus, workTreeStatus),
                    IsStaged(indexStatus),
                    originalRelativePath));
            }

            return changes;
        }

        private static bool RequiresOriginalPath(char indexStatus, char workTreeStatus)
        {
            return indexStatus is 'R' or 'C' || workTreeStatus is 'R' or 'C';
        }

        private static string ReplaceActionTokens(
            string argument,
            string workspacePath,
            GitChangeInfo? selectedChange)
        {
            return argument
                .Replace(
                    "{WorkspaceRoot}",
                    PlatformInfrastructure.NormalizePathForCurrentPlatform(workspacePath),
                    StringComparison.Ordinal)
                .Replace(
                    "{SelectedPath}",
                    ResolveSelectedPath(workspacePath, selectedChange),
                    StringComparison.Ordinal);
        }

        private static string ResolveSelectedPath(string workspacePath, GitChangeInfo? selectedChange)
        {
            return string.IsNullOrEmpty(selectedChange?.RelativePath)
                ? string.Empty
                : PlatformInfrastructure.CombinePathForCurrentPlatform(workspacePath, selectedChange.RelativePath);
        }

        private static string GetActionName(GitFileBrowserActionConfig action)
        {
            if (!string.IsNullOrWhiteSpace(action.Id))
            {
                return action.Id.Trim();
            }

            if (!string.IsNullOrWhiteSpace(action.Label))
            {
                return action.Label.Trim();
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

        private static bool IsStaged(char indexStatus)
        {
            return indexStatus is not (' ' or '?' or '!');
        }

        private static GitChangeType ToChangeType(char indexStatus, char workTreeStatus)
        {
            if (indexStatus == '?' && workTreeStatus == '?')
            {
                return GitChangeType.Untracked;
            }

            if (IsConflictStatus(indexStatus, workTreeStatus))
            {
                return GitChangeType.Conflicted;
            }

            return StatusCodeToChangeType(indexStatus) switch
            {
                GitChangeType.Unknown => StatusCodeToChangeType(workTreeStatus),
                var changeType => changeType,
            };
        }

        private static bool IsConflictStatus(char indexStatus, char workTreeStatus)
        {
            return indexStatus == 'U'
                || workTreeStatus == 'U'
                || (indexStatus == 'A' && workTreeStatus == 'A')
                || (indexStatus == 'D' && workTreeStatus == 'D');
        }

        private static GitChangeType StatusCodeToChangeType(char statusCode)
        {
            return statusCode switch
            {
                'A' => GitChangeType.Added,
                'M' => GitChangeType.Modified,
                'D' => GitChangeType.Deleted,
                'R' => GitChangeType.Renamed,
                'C' => GitChangeType.Copied,
                '?' => GitChangeType.Untracked,
                'U' => GitChangeType.Conflicted,
                _ => GitChangeType.Unknown,
            };
        }

        private sealed record GitCommandResult(bool Succeeded, string Output, string ErrorMessage);
    }
}
