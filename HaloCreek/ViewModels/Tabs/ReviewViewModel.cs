using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class ReviewViewModel : ViewModelBase, ITabSelectionAware, IDisposable
    {
        private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(10);

        private readonly ReviewSnapshotService _reviewSnapshotService;
        private readonly GitService _gitService;
        private readonly ExternalActionService _externalActionService;
        private readonly TransientEventService _transientEventService;
        private readonly DispatcherTimer _autoRefreshTimer;
        private IReadOnlyList<ReviewFilePath> _unreviewedFiles = Array.Empty<ReviewFilePath>();
        private IReadOnlyList<ReviewFilePath> _reviewedFiles = Array.Empty<ReviewFilePath>();
        private ReviewFilePath? _selectedUnreviewedFile;
        private ReviewFilePath? _selectedReviewedFile;
        private bool _isDisposed;

        public ReviewViewModel(
            ReviewSnapshotService reviewSnapshotService,
            GitService gitService,
            ExternalActionService externalActionService,
            AppCommonRuntime appCommonRuntime)
        {
            _reviewSnapshotService = reviewSnapshotService
                ?? throw new ArgumentNullException(nameof(reviewSnapshotService));
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            _externalActionService = externalActionService
                ?? throw new ArgumentNullException(nameof(externalActionService));
            ArgumentNullException.ThrowIfNull(appCommonRuntime);
            _transientEventService = appCommonRuntime.TransientEventService;
            RefreshCommand = new AsyncRelayCommand(RefreshReviewFilesAsync);
            ModifiedGit = new GitViewModel(
                _gitService,
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
            WorkspaceRuntime.Changed += OnWorkspaceChanged;
            OnWorkspaceChanged(WorkspaceRuntime.Current);
            _autoRefreshTimer = new DispatcherTimer(
                AutoRefreshInterval,
                DispatcherPriority.Background,
                OnAutoRefreshTimerTick);
            _autoRefreshTimer.Start();
        }

        public IAsyncRelayCommand RefreshCommand { get; }

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
            _autoRefreshTimer.Stop();
            _autoRefreshTimer.Tick -= OnAutoRefreshTimerTick;
            WorkspaceRuntime.Changed -= OnWorkspaceChanged;
        }

        public void OnSelected()
        {
            RequestRefreshReviewFiles();
        }

        private void OnAutoRefreshTimerTick(object? sender, EventArgs e)
        {
            if (!_isDisposed)
            {
                RequestRefreshReviewFiles();
            }
        }

        private void RequestRefreshReviewFiles()
        {
            // AsyncRelayCommand defaults to one in-flight execution, so quick repeated refresh
            // requests are ignored instead of running concurrent review index updates. If workspace
            // switching needs stale async result cancellation, keep that as a WorkspaceRuntime-bound
            // operation rather than adding per-view-model pending/generation handling here.
            if (RefreshCommand.CanExecute(null))
            {
                RefreshCommand.Execute(null);
            }
        }

        private async Task RefreshReviewFilesAsync()
        {
            try
            {
                Log.Info("Review", "RefreshReviewSnapshot invoked.");
                var result = await Task.Run(() =>
                {
                    _reviewSnapshotService.RefreshReviewSnapshot();
                    return new ReviewFilesRefreshResult(
                        _reviewSnapshotService.GetWorkingTreeAgainstReviewedFiles(),
                        _reviewSnapshotService.GetReviewedAgainstHeadFiles());
                });

                if (_isDisposed)
                {
                    return;
                }

                if (!UnreviewedFiles.SequenceEqual(result.UnreviewedFiles)
                    || !ReviewedFiles.SequenceEqual(result.ReviewedFiles))
                {
                    SelectedUnreviewedFile = null;
                    SelectedReviewedFile = null;
                    UnreviewedFiles = result.UnreviewedFiles;
                    ReviewedFiles = result.ReviewedFiles;
                }

                await ModifiedGit.RefreshChangesAsync();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                _transientEventService.ReportUserActionFailure(
                    "Review",
                    "Review files refresh failed",
                    ex.Message,
                    ex);
            }
        }

        private void OnWorkspaceChanged(WorkspaceContext workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            RequestRefreshReviewFiles();
        }

        private void AddReviewed(ReviewFilePath? file)
        {
            ArgumentNullException.ThrowIfNull(file);

            try
            {
                Log.Info("Review", $"AddReviewed invoked. File={file.RelativePath}");
                _reviewSnapshotService.MarkFileReviewed(file.RelativePath);
                Log.Info("Review", $"Marked reviewed: {file.RelativePath}");
                RequestRefreshReviewFiles();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
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
                    _reviewSnapshotService.MarkFileReviewed(file.RelativePath);
                }

                Log.Info("Review", $"Marked all reviewed. Count={files.Length}");
                RequestRefreshReviewFiles();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                RequestRefreshReviewFiles();
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
                _reviewSnapshotService.RevertWorkingTreeFileToReviewed(file.RelativePath);
                Log.Info("Review", $"Reverted to reviewed: {file.RelativePath}");
                RequestRefreshReviewFiles();
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
                    _reviewSnapshotService.RevertWorkingTreeFileToReviewed(file.RelativePath);
                }

                Log.Info("Review", $"Reverted all to reviewed. Count={files.Length}");
                RequestRefreshReviewFiles();
            }
            catch (Exception ex) when (ex is ArgumentException
                or InvalidOperationException
                or UnauthorizedAccessException
                or System.IO.IOException)
            {
                RequestRefreshReviewFiles();
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
                _reviewSnapshotService.MarkFileUnreviewed(file.RelativePath);
                Log.Info("Review", $"Marked unreviewed: {file.RelativePath}");
                RequestRefreshReviewFiles();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
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
                    _reviewSnapshotService.MarkFileUnreviewed(file.RelativePath);
                }

                Log.Info("Review", $"Marked all unreviewed. Count={files.Length}");
                RequestRefreshReviewFiles();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                RequestRefreshReviewFiles();
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
                var reviewedPath = _reviewSnapshotService.CreateTempReviewedFile(gitRelativePath);
                var workingTreePath = PlatformInfrastructure.CombinePathForCurrentPlatform(
                    WorkspaceRuntime.Current.GitRootPath,
                    gitRelativePath);
                _externalActionService.OpenDiff(
                    reviewedPath,
                    workingTreePath,
                    $"Working Tree vs Reviewed: {file.RelativePath}");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
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
                var headPath = _gitService.CreateTempHeadFile(gitRelativePath);
                var reviewedPath = _reviewSnapshotService.CreateTempReviewedFile(gitRelativePath);
                _externalActionService.OpenDiff(
                    headPath,
                    reviewedPath,
                    $"Reviewed vs HEAD: {file.RelativePath}");
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                _transientEventService.ReportUserActionFailure(
                    "Review",
                    "Diff failed",
                    ex.Message,
                    ex);
            }
        }

        private sealed record ReviewFilesRefreshResult(
            IReadOnlyList<ReviewFilePath> UnreviewedFiles,
            IReadOnlyList<ReviewFilePath> ReviewedFiles);
    }
}
