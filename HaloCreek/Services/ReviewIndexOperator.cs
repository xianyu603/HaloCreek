using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HaloCreek.Infrastructure;

namespace HaloCreek.Services
{
    public static class ReviewIndexOperator
    {
        private const string GitExecutableName = "git";
        private const string HaloCreekDirectoryName = ".HaloCreek";
        private const string HaloCreekIndexFileName = "HaloCreekIndex";

        // todo 将.HaloCreek的使用移动到一起
        public static string GetIndexPath(string workspacePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            return Path.Combine(workspacePath, HaloCreekDirectoryName, HaloCreekIndexFileName);
        }

        public static bool IsIndexAvailable(string workspacePath)
        {
            return File.Exists(GetIndexPath(workspacePath));
        }

        public static IReadOnlyList<ReviewIndexEntry> ReadEntries(string workspacePath)
        {
            if (!IsIndexAvailable(workspacePath))
            {
                return Array.Empty<ReviewIndexEntry>();
            }

            var output = RunReviewGit(
                workspacePath,
                new[] { "ls-files", "-s", "-z" });
            if (string.IsNullOrEmpty(output))
            {
                return Array.Empty<ReviewIndexEntry>();
            }

            return output
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseEntry)
                .Where(entry => entry is not null)
                .Cast<ReviewIndexEntry>()
                .ToArray();
        }

        public static void AddWorkingTreeFile(string workspacePath, string relativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            Directory.CreateDirectory(Path.Combine(workspacePath, HaloCreekDirectoryName));
            RunReviewGit(
                workspacePath,
                new[] { "update-index", "--add", "--", NormalizePath(relativePath) });
        }

        public static void RemoveFile(string workspacePath, string relativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            RunReviewGit(
                workspacePath,
                new[] { "update-index", "--force-remove", "--", NormalizePath(relativePath) });
        }

        public static void CheckoutFileToWorkingTree(string workspacePath, string relativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            RunReviewGit(
                workspacePath,
                new[] { "checkout-index", "--force", "--", NormalizePath(relativePath) });
        }

        public static string CreateTempReviewedFile(string workspacePath, string relativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            var gitRelativePath = NormalizePath(relativePath);
            var reviewedContent = RunReviewGit(
                workspacePath,
                new[] { "show", $":{gitRelativePath}" });
            var fileName = Path.GetFileName(
                PlatformInfrastructure.NormalizePathForCurrentPlatform(gitRelativePath));
            return PlatformInfrastructure.WriteTempFile(
                $"reviewed-{fileName}",
                reviewedContent);
        }

        private static string RunReviewGit(
            string workspacePath,
            IEnumerable<string> arguments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

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
                    ["GIT_INDEX_FILE"] = GetIndexPath(workspacePath),
                });
            if (result.Succeeded)
            {
                return result.Output;
            }

            var message = result.ErrorMessage.Trim();
            throw new InvalidOperationException($"Review index git command failed. {message}", result.Exception);
        }

        private static ReviewIndexEntry? ParseEntry(string token)
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

            return new ReviewIndexEntry(relativePath, blobId);
        }

        private static string NormalizePath(string relativePath)
        {
            return PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
        }
    }

    public sealed record ReviewIndexEntry(string RelativePath, string BlobId);
}
