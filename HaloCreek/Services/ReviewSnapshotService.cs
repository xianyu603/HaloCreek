using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
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
            var workspacePath = _workspaceRuntimeService.GetRequiredWorkspacePath("Select a workspace to use Review.");
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

        public void MarkFileUnreviewed(string? relativePath)
        {
            var workspacePath = _workspaceRuntimeService.GetRequiredWorkspacePath("Select a workspace to use Review.");
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
            if (!IsHaloCreekIndexAvailable(workspacePath))
            {
                Log.Warning(
                    "Review",
                    "MarkFileUnreviewed skipped because HaloCreekIndex is missing. "
                    + "This may indicate stale Review UI state or command misuse. "
                    + $"File={relativePath} Index={GetHaloCreekIndexPath(workspacePath)}");
                return;
            }

            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            RunReviewGit(
                workspacePath,
                GetHaloCreekIndexPath(workspacePath),
                new[] { "update-index", "--force-remove", "--", gitRelativePath });
        }

        public IReadOnlyList<ReviewFilePath> GetReviewedAgainstHeadFiles()
        {
            var workspacePath = _workspaceRuntimeService.GetRequiredWorkspacePath("Select a workspace to use Review.");
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
            var workspacePath = _workspaceRuntimeService.GetRequiredWorkspacePath("Select a workspace to use Review.");
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

        private string RunReviewGit(
            string workspacePath,
            string haloCreekIndexPath,
            IEnumerable<string> arguments)
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
                environmentVariables: new Dictionary<string, string?>
                {
                    ["GIT_INDEX_FILE"] = haloCreekIndexPath,
                });
            if (result.Succeeded)
            {
                return result.Output;
            }

            var message = result.ErrorMessage.Trim();
            throw new InvalidOperationException($"Review snapshot git command failed. {message}", result.Exception);
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

        private sealed record ReviewedIndexEntry(string RelativePath, string BlobId);
    }
}
