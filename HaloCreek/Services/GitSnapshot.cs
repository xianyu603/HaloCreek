using System;
using System.Collections.Generic;
using System.Linq;
using HaloCreek.Infrastructure;
using HaloCreek.Models;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services
{
    public sealed record GitSnapshot(
        string? HeadId,
        IReadOnlyList<GitSnapshotEntry> Entries,
        string Message)
        : IWorkspaceSnapshot<GitSnapshot>
    {
        public IReadOnlyList<GitChangeInfo> Changes { get; } = Entries
            .Select(entry => entry.ToChangeInfo())
            .ToArray();

        public static GitSnapshot CreateEmpty(WorkspaceContext workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            return new GitSnapshot(
                null,
                Array.Empty<GitSnapshotEntry>(),
                "No Git changes for current workspace.");
        }

        public static GitSnapshot ReadSnapshot(WorkspaceContext workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            var result = GitInfrastructure.GetChanges(workspace.GitRootPath);
            var entries = result.Changes
                .Select(change =>
                {
                    var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(
                        change.RelativePath);
                    return new GitSnapshotEntry(
                        gitRelativePath,
                        change.ChangeType,
                        change.IsStaged,
                        change.OriginalRelativePath is null
                            ? null
                            : PlatformInfrastructure.NormalizeGitRelativePath(change.OriginalRelativePath),
                        GitInfrastructure.HashWorkingTreeFile(workspace.GitRootPath, gitRelativePath),
                        GitInfrastructure.GetHeadBlobId(workspace.GitRootPath, gitRelativePath));
                })
                .ToArray();

            return new GitSnapshot(
                GitInfrastructure.GetHeadId(workspace.GitRootPath),
                entries,
                result.Message);
        }

        public static bool ContentEquals(GitSnapshot left, GitSnapshot right)
        {
            if (!string.Equals(left.HeadId, right.HeadId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(left.Message, right.Message, StringComparison.Ordinal)
                || left.Entries.Count != right.Entries.Count)
            {
                return false;
            }

            for (var index = 0; index < left.Entries.Count; index++)
            {
                if (!GitSnapshotEntry.ContentEquals(left.Entries[index], right.Entries[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed record GitSnapshotEntry(
        string RelativePath,
        GitChangeType ChangeType,
        bool IsStaged,
        string? OriginalRelativePath,
        string? WorkingTreeBlobId,
        string? HeadBlobId)
    {
        public GitChangeInfo ToChangeInfo()
        {
            return new GitChangeInfo(
                RelativePath,
                ChangeType,
                IsStaged,
                OriginalRelativePath);
        }

        public static bool ContentEquals(GitSnapshotEntry left, GitSnapshotEntry right)
        {
            return string.Equals(left.RelativePath, right.RelativePath, StringComparison.Ordinal)
                && left.ChangeType == right.ChangeType
                && left.IsStaged == right.IsStaged
                && string.Equals(
                    left.OriginalRelativePath,
                    right.OriginalRelativePath,
                    StringComparison.Ordinal)
                && string.Equals(
                    left.WorkingTreeBlobId,
                    right.WorkingTreeBlobId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    left.HeadBlobId,
                    right.HeadBlobId,
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
