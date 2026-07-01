using System;
using System.Collections.Generic;
using System.Linq;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services
{
    public sealed class ReviewIndexKeeper : IDisposable
    {
        private readonly IWorkspaceSnapshotSource<ReviewIndexSnapshot> _reviewIndexSnapshots;
        private readonly IWorkspaceSnapshotSource<GitSnapshot> _gitSnapshots;
        private bool _isDisposed;

        public ReviewIndexKeeper(
            IWorkspaceSnapshotSource<ReviewIndexSnapshot> reviewIndexSnapshots,
            IWorkspaceSnapshotSource<GitSnapshot> gitSnapshots)
        {
            _reviewIndexSnapshots = reviewIndexSnapshots
                ?? throw new ArgumentNullException(nameof(reviewIndexSnapshots));
            _gitSnapshots = gitSnapshots ?? throw new ArgumentNullException(nameof(gitSnapshots));

            _reviewIndexSnapshots.Changed += OnSnapshotChanged;
            _gitSnapshots.Changed += OnSnapshotChanged;
            RemoveOmittedEntries();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _reviewIndexSnapshots.Changed -= OnSnapshotChanged;
            _gitSnapshots.Changed -= OnSnapshotChanged;
        }

        public static IReadOnlyList<string> GetOmittedPaths(
            ReviewIndexSnapshot reviewIndexSnapshot,
            GitSnapshot gitSnapshot)
        {
            ArgumentNullException.ThrowIfNull(reviewIndexSnapshot);
            ArgumentNullException.ThrowIfNull(gitSnapshot);

            var gitEntriesByPath = gitSnapshot.Entries
                .GroupBy(entry => entry.RelativePath, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.Ordinal);

            return reviewIndexSnapshot.Entries
                .Where(entry => IsOmittedEntry(entry, gitEntriesByPath))
                .Select(entry => entry.RelativePath)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(relativePath => relativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void OnSnapshotChanged(object? sender, EventArgs e)
        {
            if (!_isDisposed)
            {
                RemoveOmittedEntries();
            }
        }

        private void RemoveOmittedEntries()
        {
            if (!_reviewIndexSnapshots.HasSuccessfulRefresh
                || !_gitSnapshots.HasSuccessfulRefresh)
            {
                return;
            }

            var reviewIndexSnapshot = _reviewIndexSnapshots.Current;
            var gitSnapshot = _gitSnapshots.Current;

            var omittedPaths = GetOmittedPaths(reviewIndexSnapshot, gitSnapshot);
            if (omittedPaths.Count == 0)
            {
                return;
            }

            foreach (var relativePath in omittedPaths)
            {
                ReviewIndexOperator.RemoveFile(WorkspaceRuntime.Current.WorkspacePath, relativePath);
                Log.Info("Review", $"ReviewIndexKeeper removed entry. File={relativePath}");
            }

            _reviewIndexSnapshots.RequestRefresh(SnapshotRefreshReason.Manual);
        }

        private static bool IsOmittedEntry(
            ReviewIndexSnapshotEntry reviewEntry,
            IReadOnlyDictionary<string, GitSnapshotEntry> gitEntriesByPath)
        {
            var relativePath = PlatformInfrastructure.NormalizeGitRelativePath(
                reviewEntry.RelativePath);
            gitEntriesByPath.TryGetValue(relativePath, out var gitEntry);

            if (gitEntry is null)
            {
                // 由于reviewed视为git变更项的子集, 没有git变更项时直接裁剪
                return true;
            }

            return reviewEntry.HeadBlobId is not null
                && string.Equals(
                    reviewEntry.ReviewedBlobId,
                    reviewEntry.HeadBlobId,
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
