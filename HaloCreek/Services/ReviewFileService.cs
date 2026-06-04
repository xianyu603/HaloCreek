using System;
using System.Collections.Generic;
using System.Linq;
using HaloCreek.Infrastructure;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class ReviewFileService
    {
        private readonly GitService _gitService;

        public ReviewFileService(GitService gitService)
        {
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        }

        public IReadOnlyList<ReviewFilePath> GetUnreviewedFiles()
        {
            var gitChanges = _gitService.GetChanges();
            return gitChanges.Changes
                .Where(IsReviewSupportedModifiedChange)
                .Select(change => new ReviewFilePath(
                    PlatformInfrastructure.NormalizeGitRelativePath(change.RelativePath)))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsReviewSupportedModifiedChange(GitChangeInfo change)
        {
            return change.ChangeType is GitChangeType.Modified
                or GitChangeType.Added
                or GitChangeType.Untracked;
        }
    }
}
