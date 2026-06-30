using System;
using System.Collections.Generic;
using System.Linq;
using HaloCreek.Infrastructure;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services
{
    public sealed record ReviewIndexSnapshot(
        IReadOnlyList<ReviewIndexSnapshotEntry> Entries)
        : IWorkspaceSnapshot<ReviewIndexSnapshot>
    {
        public static ReviewIndexSnapshot CreateEmpty()
        {
            return new ReviewIndexSnapshot(
                Array.Empty<ReviewIndexSnapshotEntry>());
        }

        public static ReviewIndexSnapshot ReadSnapshot()
        {
            var workspacePath = WorkspaceRuntime.Current.WorkspacePath;
            var entries = ReviewIndexOperator.ReadEntries(workspacePath)
                .Select(entry =>
                {
                    var relativePath = PlatformInfrastructure.NormalizeGitRelativePath(
                        entry.RelativePath);
                    return new ReviewIndexSnapshotEntry(
                        relativePath,
                        entry.BlobId,
                        GitInfrastructure.GetHeadBlobId(workspacePath, relativePath));
                })
                .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new ReviewIndexSnapshot(entries);
        }

        public static bool ContentEquals(
            ReviewIndexSnapshot left,
            ReviewIndexSnapshot right)
        {
            if (left.Entries.Count != right.Entries.Count)
            {
                return false;
            }

            for (var index = 0; index < left.Entries.Count; index++)
            {
                if (!EntryContentEquals(left.Entries[index], right.Entries[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EntryContentEquals(
            ReviewIndexSnapshotEntry left,
            ReviewIndexSnapshotEntry right)
        {
            return string.Equals(left.RelativePath, right.RelativePath, StringComparison.Ordinal)
                && string.Equals(
                    left.ReviewedBlobId,
                    right.ReviewedBlobId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    left.HeadBlobId,
                    right.HeadBlobId,
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed record ReviewIndexSnapshotEntry(
        string RelativePath,
        string ReviewedBlobId,
        string? HeadBlobId);
}
