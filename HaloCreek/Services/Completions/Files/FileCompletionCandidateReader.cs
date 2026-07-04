using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Models;

namespace HaloCreek.Services.Completions.Files
{
    internal sealed class FileCompletionCandidateReader
    {
        private const string LogCategory = "FileCompletion";

        private static readonly GitChangeType[] UncommittedChangeTypes =
        [
            GitChangeType.Modified,
            GitChangeType.Added,
            GitChangeType.Conflicted,
            GitChangeType.Untracked,
        ];

        private readonly GitService _gitService;

        public FileCompletionCandidateReader(GitService gitService)
        {
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        }

        public IReadOnlyList<string> GetUncommittedFiles()
        {
            try
            {
                var workspacePath = WorkspaceRuntime.Current.WorkspacePath;
                var relativePaths = _gitService.GetChanges().Changes
                    .Where(change => UncommittedChangeTypes.Contains(change.ChangeType))
                    .Select(change => change.RelativePath);

                return GetExistingFilesByLastWriteTimeUtc(workspacePath, relativePaths, "status");
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, $"Failed to read uncommitted file completions. {ex}");
                return Array.Empty<string>();
            }
        }

        public IReadOnlyList<string> GetRecentCommittedFiles(int commitCount)
        {
            try
            {
                var workspacePath = WorkspaceRuntime.Current.WorkspacePath;
                return GetExistingFilesByLastWriteTimeUtc(
                    workspacePath,
                    _gitService.GetRecentCommittedFilePaths(commitCount),
                    "recent committed");
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, $"Failed to read recent committed file completions. {ex}");
                return Array.Empty<string>();
            }
        }

        private static IReadOnlyList<string> GetExistingFilesByLastWriteTimeUtc(
            string workspacePath,
            IEnumerable<string> relativePaths,
            string source)
        {
            return relativePaths
                .Select(relativePath => NormalizePathOrNull(relativePath, source))
                .Where(relativePath => PlatformInfrastructure.IsExistingFileUnderDirectory(
                    workspacePath,
                    relativePath))
                .Select(relativePath => relativePath!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(relativePath => new FileCompletionCandidate(
                    relativePath,
                    File.GetLastWriteTimeUtc(PlatformInfrastructure.CombinePathForCurrentPlatform(
                        workspacePath,
                        relativePath))))
                .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
                .ThenBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(candidate => candidate.RelativePath)
                .ToArray();
        }

        private static string? NormalizePathOrNull(string relativePath, string source)
        {
            try
            {
                return PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            }
            catch (ArgumentException ex)
            {
                Log.Warning(
                    LogCategory,
                    $"Invalid Git relative path ignored. Source={source}, Path={relativePath}, Error={ex.Message}");
                return null;
            }
        }

        private sealed record FileCompletionCandidate(
            string RelativePath,
            DateTime LastWriteTimeUtc);
    }
}
