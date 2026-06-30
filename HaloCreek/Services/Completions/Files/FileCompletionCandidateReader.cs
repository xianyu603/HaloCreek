using System;
using System.Collections.Generic;
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
                return _gitService.GetChanges().Changes
                    .Where(change => UncommittedChangeTypes.Contains(change.ChangeType))
                    .Select(change => NormalizePathOrNull(change.RelativePath, "status"))
                    .Where(relativePath => PlatformInfrastructure.IsExistingFileUnderDirectory(
                        workspacePath,
                        relativePath))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(relativePath => relativePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray()!;
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
                return _gitService.GetRecentCommittedFilePaths(commitCount)
                    .Select(relativePath => NormalizePathOrNull(relativePath, "recent committed"))
                    .Where(relativePath => PlatformInfrastructure.IsExistingFileUnderDirectory(
                        workspacePath,
                        relativePath))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(relativePath => relativePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray()!;
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, $"Failed to read recent committed file completions. {ex}");
                return Array.Empty<string>();
            }
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
    }
}
