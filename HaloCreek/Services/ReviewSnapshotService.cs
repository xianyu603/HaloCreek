using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HaloCreek.Infrastructure;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class ReviewSnapshotService
    {
        private const string GitExecutableName = "git";
        private const string HaloCreekDirectoryName = ".HaloCreek";
        private const string HaloCreekIndexFileName = "HaloCreekIndex";

        private readonly WorkspaceRuntimeService _workspaceRuntimeService;
        private readonly GitService _gitService;

        public ReviewSnapshotService(
            WorkspaceRuntimeService workspaceRuntimeService,
            GitService gitService)
        {
            _workspaceRuntimeService = workspaceRuntimeService
                ?? throw new ArgumentNullException(nameof(workspaceRuntimeService));
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        }

        public void MarkFileReviewed(string? relativePath)
        {
            var workspacePath = GetRequiredWorkspacePath();
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            var absoluteWorkingTreePath = PlatformInfrastructure.CombinePathForCurrentPlatform(
                workspacePath,
                gitRelativePath);
            if (!File.Exists(absoluteWorkingTreePath) || Directory.Exists(absoluteWorkingTreePath))
            {
                throw new InvalidOperationException("File does not exist in working tree.");
            }

            var workingTreeBlobId = _gitService.HashWorkingTreeFile(gitRelativePath);
            if (workingTreeBlobId is null)
            {
                throw new InvalidOperationException("File does not exist in working tree.");
            }

            var headBlobId = _gitService.GetHeadBlobId(gitRelativePath);
            if (headBlobId is not null
                && string.Equals(headBlobId, workingTreeBlobId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Directory.CreateDirectory(Path.Combine(workspacePath, HaloCreekDirectoryName));
            var haloCreekIndexPath = GetHaloCreekIndexPath(workspacePath);
            RunReviewGit(
                workspacePath,
                haloCreekIndexPath,
                new[] { "update-index", "--add", "--", gitRelativePath });
        }

        public IReadOnlyList<ReviewFilePath> GetReviewedAgainstHeadFiles()
        {
            var workspacePath = GetRequiredWorkspacePath();
            if (!IsHaloCreekIndexAvailable(workspacePath))
            {
                return Array.Empty<ReviewFilePath>();
            }

            return ReadReviewedIndexEntries(workspacePath)
                .Where(entry =>
                {
                    var headBlobId = _gitService.GetHeadBlobId(entry.RelativePath);
                    return headBlobId is null
                        || !string.Equals(headBlobId, entry.BlobId, StringComparison.OrdinalIgnoreCase);
                })
                .Select(entry => new ReviewFilePath(entry.RelativePath))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IReadOnlyList<ReviewFilePath> GetWorkingTreeAgainstReviewedFiles()
        {
            var workspacePath = GetRequiredWorkspacePath();
            var reviewedEntries = ReadReviewedIndexEntries(workspacePath)
                .GroupBy(entry => entry.RelativePath, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().BlobId,
                    StringComparer.Ordinal);
            var gitChanges = _gitService.GetChanges();

            return gitChanges.Changes
                .Where(IsReviewSupportedWorkingTreeChange)
                .Where(change => IsWorkingTreeDifferentFromReviewed(
                    workspacePath,
                    change.RelativePath,
                    reviewedEntries))
                .Select(change => new ReviewFilePath(
                    PlatformInfrastructure.NormalizeGitRelativePath(change.RelativePath)))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string GetHaloCreekIndexPath(string workspacePath)
        {
            return Path.Combine(workspacePath, HaloCreekDirectoryName, HaloCreekIndexFileName);
        }

        private static bool IsHaloCreekIndexAvailable(string workspacePath)
        {
            return File.Exists(GetHaloCreekIndexPath(workspacePath));
        }

        private IReadOnlyList<ReviewedIndexEntry> ReadReviewedIndexEntries(string workspacePath)
        {
            if (!IsHaloCreekIndexAvailable(workspacePath))
            {
                return Array.Empty<ReviewedIndexEntry>();
            }

            var output = RunReviewGit(
                workspacePath,
                GetHaloCreekIndexPath(workspacePath),
                new[] { "ls-files", "-s", "-z" });
            if (string.IsNullOrEmpty(output))
            {
                return Array.Empty<ReviewedIndexEntry>();
            }

            return output
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseReviewedIndexEntry)
                .Where(entry => entry is not null)
                .Cast<ReviewedIndexEntry>()
                .ToArray();
        }

        // TODO 这个是不是中转给GitService比较好?
        private string RunReviewGit(
            string workspacePath,
            string haloCreekIndexPath,
            IEnumerable<string> arguments)
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
                process.StartInfo.Environment["GIT_INDEX_FILE"] = haloCreekIndexPath;
                process.StartInfo.ArgumentList.Add("-C");
                process.StartInfo.ArgumentList.Add(workspacePath);
                foreach (var argument in arguments)
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    return output;
                }

                throw new InvalidOperationException(GetGitFailureMessage(
                    "Review snapshot git command failed.",
                    error.Trim()));
            }
            catch (Exception ex) when (ex is Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }
        }

        private bool IsWorkingTreeDifferentFromReviewed(
            string workspacePath,
            string relativePath,
            IReadOnlyDictionary<string, string> reviewedEntries)
        {
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            var absoluteWorkingTreePath = PlatformInfrastructure.CombinePathForCurrentPlatform(
                workspacePath,
                gitRelativePath);
            if (!File.Exists(absoluteWorkingTreePath) || Directory.Exists(absoluteWorkingTreePath))
            {
                return false;
            }

            var workingTreeBlobId = _gitService.HashWorkingTreeFile(gitRelativePath);
            if (workingTreeBlobId is null)
            {
                return false;
            }

            var reviewedBlobId = reviewedEntries.TryGetValue(gitRelativePath, out var reviewedEntryBlobId)
                ? reviewedEntryBlobId
                : _gitService.GetHeadBlobId(gitRelativePath);

            return reviewedBlobId is null
                || !string.Equals(workingTreeBlobId, reviewedBlobId, StringComparison.OrdinalIgnoreCase);
        }

        // TODO 考虑将这个模式(空就抛)写到_workspaceRuntimeService
        private string GetRequiredWorkspacePath()
        {
            var workspacePath = _workspaceRuntimeService.CurrentWorkspacePath;
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new InvalidOperationException("Select a workspace to use Review.");
            }

            return workspacePath;
        }

        private static ReviewedIndexEntry? ParseReviewedIndexEntry(string token)
        {
            var tabIndex = token.IndexOf('\t', StringComparison.Ordinal);
            if (tabIndex <= 0 || tabIndex == token.Length - 1)
            {
                return null;
            }

            var metadata = token[..tabIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (metadata.Length < 3)
            {
                return null;
            }

            var blobId = metadata[1];
            var relativePath = token[(tabIndex + 1)..];
            if (string.IsNullOrWhiteSpace(blobId) || string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            return new ReviewedIndexEntry(relativePath, blobId);
        }

        private static bool IsReviewSupportedWorkingTreeChange(GitChangeInfo change)
        {
            return change.ChangeType is GitChangeType.Modified
                or GitChangeType.Added
                or GitChangeType.Untracked;
        }

        private static string GetGitFailureMessage(string fallbackMessage, string gitMessage)
        {
            if (string.IsNullOrWhiteSpace(gitMessage))
            {
                return fallbackMessage;
            }

            if (gitMessage.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
            {
                return "Current workspace is not a Git repository.";
            }

            return gitMessage;
        }

        private sealed record ReviewedIndexEntry(string RelativePath, string BlobId);
    }
}
