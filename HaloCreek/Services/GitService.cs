using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class GitService
    {
        private const string GitExecutableName = "git";

        public GitChangesResult GetChanges(string? workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new GitChangesResult(
                    Array.Empty<GitChangeInfo>(),
                    "Select a workspace to view Git changes.");
            }

            string normalizedWorkspacePath;
            try
            {
                normalizedWorkspacePath = Path.GetFullPath(workspacePath.Trim());
            }
            catch (Exception ex) when (ex is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                return new GitChangesResult(
                    Array.Empty<GitChangeInfo>(),
                    $"Invalid workspace path: {workspacePath}");
            }

            if (!Directory.Exists(normalizedWorkspacePath))
            {
                return new GitChangesResult(
                    Array.Empty<GitChangeInfo>(),
                    $"Workspace path does not exist: {normalizedWorkspacePath}");
            }

            var commandResult = RunGitStatus(normalizedWorkspacePath);
            if (!commandResult.Succeeded)
            {
                var message = string.IsNullOrWhiteSpace(commandResult.ErrorMessage)
                    ? "Git status failed."
                    : commandResult.ErrorMessage.Trim();
                if (message.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Current workspace is not a Git repository.";
                }

                return new GitChangesResult(Array.Empty<GitChangeInfo>(), message);
            }

            var changes = ParsePorcelainStatus(commandResult.Output)
                .OrderBy(change => change.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(change => change.OriginalRelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var loadedMessage = changes.Length == 0
                ? "No Git changes for current workspace."
                : $"Loaded {changes.Length} Git changes.";

            return new GitChangesResult(changes, loadedMessage);
        }

        public bool TryOpenDiffTool(string workspacePath, GitChangeInfo change)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
            ArgumentNullException.ThrowIfNull(change);

            return false;
        }

        public bool TryCommit(string workspacePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
            return false;
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
