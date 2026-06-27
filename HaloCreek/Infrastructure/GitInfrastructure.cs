using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.Infrastructure
{
    // 静态使用全局workspace path, 由外界保证调用前后workspace的一致性
    public static class GitInfrastructure
    {
        private const string GitExecutableName = "git";

        public static GitChangesResult GetChanges()
        {
            var workspacePath = WorkspaceRuntime.Current.GitRootPath;

            var commandResult = RunGitStatus(workspacePath);
            if (!commandResult.Succeeded)
            {
                var message = string.IsNullOrWhiteSpace(commandResult.ErrorMessage)
                    ? "Git status failed."
                    : commandResult.ErrorMessage.Trim();

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

        public static IReadOnlyList<string> GetRecentCommittedFilePaths(int commitCount)
        {
            if (commitCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(commitCount), "Commit count must be positive.");
            }

            var workspacePath = WorkspaceRuntime.Current.GitRootPath;
            var commandResult = RunGit(
                workspacePath,
                new[] { "log", "-n", commitCount.ToString(), "--name-only", "--pretty=format:" });
            if (!commandResult.Succeeded)
            {
                var message = commandResult.ErrorMessage.Trim();
                throw new InvalidOperationException($"Git recent committed file query failed. {message}");
            }

            return commandResult.Output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
        }

        public static async IAsyncEnumerable<string> StreamWorkspaceFilePaths(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var relativePath in StreamWorkspaceFilePaths(
                WorkspaceRuntime.Current.GitRootPath,
                cancellationToken))
            {
                yield return relativePath;
            }
        }

        public static async IAsyncEnumerable<string> StreamWorkspaceFilePaths(
            string workspacePath,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            var gitArguments = new List<string>
            {
                "-C",
                workspacePath,
                "ls-files",
                "--cached",
                "--others",
                "--exclude-standard",
                "-z",
            };

            await foreach (var relativePath in PlatformInfrastructure.StreamNullSeparatedProcessOutput(
                    GitExecutableName,
                    gitArguments,
                    cancellationToken: cancellationToken))
            {
                yield return relativePath;
            }
        }

        public static string? GetHeadBlobId(string? relativePath)
        {
            var workspacePath = WorkspaceRuntime.Current.GitRootPath;
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            var commandResult = RunGit(
                workspacePath,
                new[] { "rev-parse", $"HEAD:{gitRelativePath}" });
            if (commandResult.Succeeded)
            {
                return NormalizeBlobId(commandResult.Output);
            }

            var message = commandResult.ErrorMessage.Trim();
            if (IsMissingHeadPathError(message))
            {
                return null;
            }

            throw new InvalidOperationException($"Git HEAD blob query failed. {message}");
        }

        public static string? HashWorkingTreeFile(string? relativePath)
        {
            var workspacePath = WorkspaceRuntime.Current.GitRootPath;
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            var absoluteFilePath = PlatformInfrastructure.CombinePathForCurrentPlatform(
                workspacePath,
                gitRelativePath);
            if (!File.Exists(absoluteFilePath) || Directory.Exists(absoluteFilePath))
            {
                return null;
            }

            var commandResult = RunGit(
                workspacePath,
                new[] { "hash-object", $"--path={gitRelativePath}", "--", absoluteFilePath });
            if (commandResult.Succeeded)
            {
                return NormalizeBlobId(commandResult.Output);
            }

            var message = commandResult.ErrorMessage.Trim();
            throw new InvalidOperationException($"Git working tree blob hash failed. {message}");
        }

        public static string CreateTempHeadFile(string? relativePath)
        {
            var workspacePath = WorkspaceRuntime.Current.GitRootPath;
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            var commandResult = RunGit(
                workspacePath,
                new[] { "show", $"HEAD:{gitRelativePath}" });
            if (commandResult.Succeeded)
            {
                return WriteTempGitContent("head", gitRelativePath, commandResult.Output);
            }

            var message = commandResult.ErrorMessage.Trim();
            if (IsMissingHeadPathError(message))
            {
                return WriteTempGitContent("head", gitRelativePath, string.Empty);
            }

            throw new InvalidOperationException($"Git HEAD file query failed. {message}");
        }

        public static void RestoreFileFromHead(string? relativePath)
        {
            var workspacePath = WorkspaceRuntime.Current.GitRootPath;
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            var commandResult = RunGit(
                workspacePath,
                new[] { "restore", "--source=HEAD", "--worktree", "--", gitRelativePath });
            if (commandResult.Succeeded)
            {
                return;
            }

            var message = commandResult.ErrorMessage.Trim();
            throw new InvalidOperationException($"Git HEAD file restore failed. {message}");
        }

        private static GitCommandResult RunGitStatus(string workspacePath)
        {
            return RunGit(
                workspacePath,
                new[] { "status", "--porcelain=v1", "-z", "--untracked-files=all" });
        }

        private static GitCommandResult RunGit(
            string workspacePath,
            IEnumerable<string> arguments,
            IReadOnlyDictionary<string, string?>? environmentVariables = null)
        {
            var gitArguments = new List<string>
            {
                "-C",
                workspacePath,
            };
            gitArguments.AddRange(arguments);

            var result = PlatformInfrastructure.RunProcessWithCapturedOutput(
                GitExecutableName,
                gitArguments,
                environmentVariables: environmentVariables);

            return new GitCommandResult(result.Succeeded, result.Output, result.ErrorMessage);
        }

        private static string? NormalizeBlobId(string output)
        {
            var blobId = output.Trim();
            return string.IsNullOrWhiteSpace(blobId)
                ? null
                : blobId;
        }

        private static string WriteTempGitContent(
            string prefix,
            string gitRelativePath,
            string content)
        {
            var fileName = Path.GetFileName(
                PlatformInfrastructure.NormalizePathForCurrentPlatform(gitRelativePath));
            return PlatformInfrastructure.WriteTempFile(
                $"{prefix}-{fileName}",
                content);
        }

        private static bool IsMissingHeadPathError(string message)
        {
            return message.Contains("does not exist in", StringComparison.OrdinalIgnoreCase)
                || message.Contains("exists on disk, but not in", StringComparison.OrdinalIgnoreCase)
                || message.Contains("invalid object name 'HEAD'", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ambiguous argument 'HEAD:", StringComparison.OrdinalIgnoreCase);
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
