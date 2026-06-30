using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class ReviewViewModel : ViewModelBase, ITabSelectionAware, IDisposable
    {
        private readonly ExternalActionService _externalActionService;
        private readonly IWorkspaceSnapshotSource<GitSnapshot> _gitSnapshots;
        private readonly IWorkspaceSnapshotSource<ReviewIndexSnapshot> _reviewIndexSnapshots;
        private readonly TransientEventService _transientEventService;
        private IReadOnlyList<ReviewFilePath> _unreviewedFiles = Array.Empty<ReviewFilePath>();
        private IReadOnlyList<ReviewFilePath> _reviewedFiles = Array.Empty<ReviewFilePath>();
        private HashSet<string> _omittedPaths = new(StringComparer.Ordinal);
        private ReviewFilePath? _selectedUnreviewedFile;
        private ReviewFilePath? _selectedReviewedFile;
        private bool _isDisposed;

        public ReviewViewModel(
            ExternalActionService externalActionService,
            IWorkspaceSnapshotSource<GitSnapshot> gitSnapshots,
            IWorkspaceSnapshotSource<ReviewIndexSnapshot> reviewIndexSnapshots,
            AppCommonRuntime appCommonRuntime)
        {
            _externalActionService = externalActionService
                ?? throw new ArgumentNullException(nameof(externalActionService));
            _gitSnapshots = gitSnapshots ?? throw new ArgumentNullException(nameof(gitSnapshots));
            _reviewIndexSnapshots = reviewIndexSnapshots
                ?? throw new ArgumentNullException(nameof(reviewIndexSnapshots));
            ArgumentNullException.ThrowIfNull(appCommonRuntime);
            _transientEventService = appCommonRuntime.TransientEventService;
            RefreshCommand = new RelayCommand(RequestSnapshotRefresh);
            ModifiedGit = new GitViewModel(
                gitSnapshots,
                _externalActionService,
                appCommonRuntime,
                RefreshCommand);
            AddReviewedCommand = new RelayCommand<ReviewFilePath>(AddReviewed);
            RevertToReviewedCommand = new RelayCommand<ReviewFilePath>(RevertToReviewed);
            MarkUnreviewedCommand = new RelayCommand<ReviewFilePath>(MarkUnreviewed);
            MarkAllReviewedCommand = new RelayCommand(MarkAllReviewed, HasUnreviewedFiles);
            RevertAllToReviewedCommand = new RelayCommand(RevertAllToReviewed, HasUnreviewedFiles);
            MarkAllUnreviewedCommand = new RelayCommand(MarkAllUnreviewed, HasReviewedFiles);
            DiffUnreviewedAgainstReviewedCommand = new RelayCommand<ReviewFilePath>(
                DiffWorkingTreeAgainstReviewed);
            DiffReviewedAgainstWorkingTreeCommand = new RelayCommand<ReviewFilePath>(
                DiffWorkingTreeAgainstReviewed);
            DiffReviewedAgainstHeadCommand = new RelayCommand<ReviewFilePath>(
                DiffReviewedAgainstHead);
            _gitSnapshots.Changed += OnSnapshotChanged;
            _reviewIndexSnapshots.Changed += OnSnapshotChanged;
            RefreshReviewFilesFromSnapshots();
        }

        public IRelayCommand RefreshCommand { get; }

        public IRelayCommand<ReviewFilePath> AddReviewedCommand { get; }

        public IRelayCommand<ReviewFilePath> RevertToReviewedCommand { get; }

        public IRelayCommand<ReviewFilePath> MarkUnreviewedCommand { get; }

        public IRelayCommand MarkAllReviewedCommand { get; }

        public IRelayCommand RevertAllToReviewedCommand { get; }

        public IRelayCommand MarkAllUnreviewedCommand { get; }

        public IRelayCommand<ReviewFilePath> DiffUnreviewedAgainstReviewedCommand { get; }

        public IRelayCommand<ReviewFilePath> DiffReviewedAgainstWorkingTreeCommand { get; }

        public IRelayCommand<ReviewFilePath> DiffReviewedAgainstHeadCommand { get; }

        public GitViewModel ModifiedGit { get; }

        public IReadOnlyList<ReviewFilePath> UnreviewedFiles
        {
            get => _unreviewedFiles;
            private set
            {
                if (SetProperty(ref _unreviewedFiles, value))
                {
                    MarkAllReviewedCommand.NotifyCanExecuteChanged();
                    RevertAllToReviewedCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public IReadOnlyList<ReviewFilePath> ReviewedFiles
        {
            get => _reviewedFiles;
            private set
            {
                if (SetProperty(ref _reviewedFiles, value))
                {
                    MarkAllUnreviewedCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public ReviewFilePath? SelectedUnreviewedFile
        {
            get => _selectedUnreviewedFile;
            set => SetProperty(ref _selectedUnreviewedFile, value);
        }

        public ReviewFilePath? SelectedReviewedFile
        {
            get => _selectedReviewedFile;
            set => SetProperty(ref _selectedReviewedFile, value);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _gitSnapshots.Changed -= OnSnapshotChanged;
            _reviewIndexSnapshots.Changed -= OnSnapshotChanged;
            ModifiedGit.Dispose();
        }

        public void OnSelected()
        {
            RequestSnapshotRefresh();
        }

        private void RequestSnapshotRefresh()
        {
            _gitSnapshots.RequestRefresh(SnapshotRefreshReason.Manual);
            _reviewIndexSnapshots.RequestRefresh(SnapshotRefreshReason.Manual);
        }

        private void OnSnapshotChanged(object? sender, EventArgs e)
        {
            if (!_isDisposed)
            {
                RefreshReviewFilesFromSnapshots();
            }
        }

        private void RefreshReviewFilesFromSnapshots()
        {
            var gitSnapshot = _gitSnapshots.Current;
            var reviewIndexSnapshot = _reviewIndexSnapshots.Current;
            _omittedPaths = ReviewIndexKeeper.GetOmittedPaths(reviewIndexSnapshot, gitSnapshot)
                .ToHashSet(StringComparer.Ordinal);
            var result = BuildReviewFiles(
                gitSnapshot,
                reviewIndexSnapshot,
                _omittedPaths);
            if (!UnreviewedFiles.SequenceEqual(result.UnreviewedFiles)
                || !ReviewedFiles.SequenceEqual(result.ReviewedFiles))
            {
                SelectedUnreviewedFile = null;
                SelectedReviewedFile = null;
                UnreviewedFiles = result.UnreviewedFiles;
                ReviewedFiles = result.ReviewedFiles;
            }
        }

        private void AddReviewed(ReviewFilePath? file)
        {
            ArgumentNullException.ThrowIfNull(file);

            try
            {
                Log.Info("Review", $"AddReviewed invoked. File={file.RelativePath}");
                MarkFileReviewed(file.RelativePath);
                Log.Info("Review", $"Marked reviewed: {file.RelativePath}");
                RequestSnapshotRefresh();
            }
            catch (Exception ex) when (ex is ArgumentException
                or InvalidOperationException
                or UnauthorizedAccessException
                or IOException)
            {
                _transientEventService.ReportUserActionFailure(
                    "Review",
                    "Add reviewed failed",
                    ex.Message,
                    ex);
            }
        }

        private bool HasUnreviewedFiles()
        {
            return UnreviewedFiles.Count > 0;
        }

        private void MarkAllReviewed()
        {
            var files = UnreviewedFiles.ToArray();

            try
            {
                Log.Info("Review", $"MarkAllReviewed invoked. Count={files.Length}");
                foreach (var file in files)
                {
                    MarkFileReviewed(file.RelativePath);
                }

                Log.Info("Review", $"Marked all reviewed. Count={files.Length}");
                RequestSnapshotRefresh();
            }
            catch (Exception ex) when (ex is ArgumentException
                or InvalidOperationException
                or UnauthorizedAccessException
                or IOException)
            {
                RequestSnapshotRefresh();
                _transientEventService.ReportUserActionFailure(
                    "Review",
                    "Mark all reviewed failed",
                    ex.Message,
                    ex);
            }
        }

        private void RevertToReviewed(ReviewFilePath? file)
        {
            ArgumentNullException.ThrowIfNull(file);

            try
            {
                Log.Info("Review", $"RevertToReviewed invoked. File={file.RelativePath}");
                RevertWorkingTreeFileToReviewed(file.RelativePath);
                Log.Info("Review", $"Reverted to reviewed: {file.RelativePath}");
                RequestSnapshotRefresh();
            }
            catch (Exception ex) when (ex is ArgumentException
                or InvalidOperationException
                or UnauthorizedAccessException
                or System.IO.IOException)
            {
                _transientEventService.ReportUserActionFailure(
                    "Review",
                    "Revert to reviewed failed",
                    ex.Message,
                    ex);
            }
        }

        private void RevertAllToReviewed()
        {
            var files = UnreviewedFiles.ToArray();

            try
            {
                Log.Info("Review", $"RevertAllToReviewed invoked. Count={files.Length}");
                foreach (var file in files)
                {
                    RevertWorkingTreeFileToReviewed(file.RelativePath);
                }

                Log.Info("Review", $"Reverted all to reviewed. Count={files.Length}");
                RequestSnapshotRefresh();
            }
            catch (Exception ex) when (ex is ArgumentException
                or InvalidOperationException
                or UnauthorizedAccessException
                or System.IO.IOException)
            {
                RequestSnapshotRefresh();
                _transientEventService.ReportUserActionFailure(
                    "Review",
                    "Revert all to reviewed failed",
                    ex.Message,
                    ex);
            }
        }

        private void MarkUnreviewed(ReviewFilePath? file)
        {
            ArgumentNullException.ThrowIfNull(file);

            try
            {
                Log.Info("Review", $"MarkUnreviewed invoked. File={file.RelativePath}");
                MarkFileUnreviewed(file.RelativePath);
                Log.Info("Review", $"Marked unreviewed: {file.RelativePath}");
                RequestSnapshotRefresh();
            }
            catch (Exception ex) when (ex is ArgumentException
                or InvalidOperationException
                or UnauthorizedAccessException
                or IOException)
            {
                _transientEventService.ReportUserActionFailure(
                    "Review",
                    "Mark unreviewed failed",
                    ex.Message,
                    ex);
            }
        }

        private bool HasReviewedFiles()
        {
            return ReviewedFiles.Count > 0;
        }

        private void MarkAllUnreviewed()
        {
            var files = ReviewedFiles.ToArray();

            try
            {
                Log.Info("Review", $"MarkAllUnreviewed invoked. Count={files.Length}");
                foreach (var file in files)
                {
                    MarkFileUnreviewed(file.RelativePath);
                }

                Log.Info("Review", $"Marked all unreviewed. Count={files.Length}");
                RequestSnapshotRefresh();
            }
            catch (Exception ex) when (ex is ArgumentException
                or InvalidOperationException
                or UnauthorizedAccessException
                or IOException)
            {
                RequestSnapshotRefresh();
                _transientEventService.ReportUserActionFailure(
                    "Review",
                    "Mark all unreviewed failed",
                    ex.Message,
                    ex);
            }
        }

        private void DiffWorkingTreeAgainstReviewed(ReviewFilePath? file)
        {
            ArgumentNullException.ThrowIfNull(file);

            try
            {
                Log.Info("Review", $"DiffWorkingTreeAgainstReviewed invoked. File={file.RelativePath}");
                var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(file.RelativePath);
                var reviewedPath = CreateReviewedTempFile(gitRelativePath);
                var workingTreePath = PlatformInfrastructure.CombinePathForCurrentPlatform(
                    WorkspaceRuntime.Current.WorkspacePath,
                    gitRelativePath);
                _externalActionService.OpenDiff(
                    reviewedPath,
                    workingTreePath,
                    $"Working Tree vs Reviewed: {file.RelativePath}");
            }
            catch (Exception ex) when (ex is ArgumentException
                or InvalidOperationException
                or UnauthorizedAccessException
                or IOException)
            {
                _transientEventService.ReportUserActionFailure(
                    "Review",
                    "Diff failed",
                    ex.Message,
                    ex);
            }
        }

        private void DiffReviewedAgainstHead(ReviewFilePath? file)
        {
            ArgumentNullException.ThrowIfNull(file);

            try
            {
                Log.Info("Review", $"DiffReviewedAgainstHead invoked. File={file.RelativePath}");
                var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(file.RelativePath);
                var reviewEntry = GetActiveReviewEntry(gitRelativePath)
                    ?? throw new InvalidOperationException("Reviewed file is no longer available.");
                var headPath = reviewEntry.HeadBlobId is null
                    ? CreateEmptyHeadTempFile(gitRelativePath)
                    : GitInfrastructure.CreateTempHeadFile(gitRelativePath);
                var reviewedPath = ReviewIndexOperator.CreateTempReviewedFile(
                    WorkspaceRuntime.Current.WorkspacePath,
                    gitRelativePath);
                _externalActionService.OpenDiff(
                    headPath,
                    reviewedPath,
                    $"Reviewed vs HEAD: {file.RelativePath}");
            }
            catch (Exception ex) when (ex is ArgumentException
                or InvalidOperationException
                or UnauthorizedAccessException
                or IOException)
            {
                _transientEventService.ReportUserActionFailure(
                    "Review",
                    "Diff failed",
                    ex.Message,
                    ex);
            }
        }

        private void MarkFileReviewed(string relativePath)
        {
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            var gitEntry = GetGitEntry(gitRelativePath)
                ?? throw new InvalidOperationException("File is no longer modified in the current snapshot.");
            if (!IsReviewSupportedWorkingTreeChange(gitEntry) || gitEntry.WorkingTreeBlobId is null)
            {
                throw new InvalidOperationException("File is no longer available in the working tree snapshot.");
            }

            var reviewEntry = GetActiveReviewEntry(gitRelativePath);
            var baselineBlobId = reviewEntry?.ReviewedBlobId ?? gitEntry.HeadBlobId;
            if (baselineBlobId is not null
                && string.Equals(
                    baselineBlobId,
                    gitEntry.WorkingTreeBlobId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ReviewIndexOperator.AddWorkingTreeFile(
                WorkspaceRuntime.Current.WorkspacePath,
                gitRelativePath);
        }

        private void MarkFileUnreviewed(string relativePath)
        {
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            if (GetReviewEntry(gitRelativePath) is null)
            {
                Log.Warning(
                    "Review",
                    $"MarkFileUnreviewed skipped because snapshot has no review entry. File={gitRelativePath}");
                return;
            }

            ReviewIndexOperator.RemoveFile(
                WorkspaceRuntime.Current.WorkspacePath,
                gitRelativePath);
        }

        private void RevertWorkingTreeFileToReviewed(string relativePath)
        {
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            var reviewEntry = GetActiveReviewEntry(gitRelativePath);
            if (reviewEntry is not null)
            {
                var absoluteWorkingTreePath = PlatformInfrastructure.CombinePathForCurrentPlatform(
                    WorkspaceRuntime.Current.WorkspacePath,
                    gitRelativePath);
                var parentDirectory = Path.GetDirectoryName(absoluteWorkingTreePath);
                if (!string.IsNullOrWhiteSpace(parentDirectory))
                {
                    Directory.CreateDirectory(parentDirectory);
                }

                ReviewIndexOperator.CheckoutFileToWorkingTree(
                    WorkspaceRuntime.Current.WorkspacePath,
                    gitRelativePath);
                return;
            }

            var gitEntry = GetGitEntry(gitRelativePath)
                ?? throw new InvalidOperationException("File is no longer modified in the current snapshot.");
            if (gitEntry.HeadBlobId is not null)
            {
                GitInfrastructure.RestoreFileFromHead(gitRelativePath);
                return;
            }

            if (gitEntry.WorkingTreeBlobId is null)
            {
                throw new InvalidOperationException("File is no longer available in the working tree snapshot.");
            }

            File.Delete(PlatformInfrastructure.CombinePathForCurrentPlatform(
                WorkspaceRuntime.Current.WorkspacePath,
                gitRelativePath));
        }

        private string CreateReviewedTempFile(string relativePath)
        {
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            if (GetActiveReviewEntry(gitRelativePath) is not null)
            {
                return ReviewIndexOperator.CreateTempReviewedFile(
                    WorkspaceRuntime.Current.WorkspacePath,
                    gitRelativePath);
            }

            var gitEntry = GetGitEntry(gitRelativePath)
                ?? throw new InvalidOperationException("File is no longer modified in the current snapshot.");
            return gitEntry.HeadBlobId is null
                ? CreateEmptyHeadTempFile(gitRelativePath)
                : GitInfrastructure.CreateTempHeadFile(gitRelativePath);
        }

        private ReviewIndexSnapshotEntry? GetReviewEntry(string relativePath)
        {
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            return _reviewIndexSnapshots.Current.Entries.FirstOrDefault(entry => string.Equals(
                entry.RelativePath,
                gitRelativePath,
                StringComparison.Ordinal));
        }

        private ReviewIndexSnapshotEntry? GetActiveReviewEntry(string relativePath)
        {
            var reviewEntry = GetReviewEntry(relativePath);
            if (reviewEntry is null)
            {
                return null;
            }

            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            return _omittedPaths.Contains(gitRelativePath)
                ? null
                : reviewEntry;
        }

        private GitSnapshotEntry? GetGitEntry(string relativePath)
        {
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            return _gitSnapshots.Current.Entries.FirstOrDefault(entry => string.Equals(
                entry.RelativePath,
                gitRelativePath,
                StringComparison.Ordinal));
        }

        private static ReviewFilesRefreshResult BuildReviewFiles(
            GitSnapshot gitSnapshot,
            ReviewIndexSnapshot reviewIndexSnapshot,
            IReadOnlySet<string> omittedPaths)
        {
            var reviewEntriesByPath = reviewIndexSnapshot.Entries
                .Where(entry => !omittedPaths.Contains(entry.RelativePath))
                .GroupBy(entry => entry.RelativePath, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.Ordinal);

            var unreviewedFiles = gitSnapshot.Entries
                .Where(IsReviewSupportedWorkingTreeChange)
                .Where(entry => entry.WorkingTreeBlobId is not null)
                .Where(entry =>
                {
                    var baselineBlobId = reviewEntriesByPath.TryGetValue(
                        entry.RelativePath,
                        out var reviewEntry)
                            ? reviewEntry.ReviewedBlobId
                            : entry.HeadBlobId;
                    return baselineBlobId is null
                        || !string.Equals(
                            baselineBlobId,
                            entry.WorkingTreeBlobId,
                            StringComparison.OrdinalIgnoreCase);
                })
                .Select(entry => new ReviewFilePath(entry.RelativePath))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var reviewedFiles = reviewIndexSnapshot.Entries
                .Where(entry => !omittedPaths.Contains(entry.RelativePath))
                .Where(entry => entry.HeadBlobId is null
                    || !string.Equals(
                        entry.ReviewedBlobId,
                        entry.HeadBlobId,
                        StringComparison.OrdinalIgnoreCase))
                .Select(entry => new ReviewFilePath(entry.RelativePath))
                .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new ReviewFilesRefreshResult(unreviewedFiles, reviewedFiles);
        }

        private static bool IsReviewSupportedWorkingTreeChange(GitSnapshotEntry entry)
        {
            return entry.ChangeType is GitChangeType.Modified
                or GitChangeType.Added
                or GitChangeType.Untracked;
        }

        private static string CreateEmptyHeadTempFile(string relativePath)
        {
            var gitRelativePath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
            var fileName = Path.GetFileName(
                PlatformInfrastructure.NormalizePathForCurrentPlatform(gitRelativePath));
            return PlatformInfrastructure.WriteTempFile($"head-{fileName}", string.Empty);
        }

        private sealed record ReviewFilesRefreshResult(
            IReadOnlyList<ReviewFilePath> UnreviewedFiles,
            IReadOnlyList<ReviewFilePath> ReviewedFiles);
    }
}
