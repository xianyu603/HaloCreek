using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
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
        private static readonly IBrush MatchedForeground = new SolidColorBrush(Color.Parse("#166534"));
        private static readonly IBrush UnmatchedForeground = new SolidColorBrush(Color.Parse("#854D0E"));
        private static readonly IBrush FrontSessionForeground = new SolidColorBrush(Color.Parse("#334155"));
        private static readonly IBrush DisabledForeground = new SolidColorBrush(Color.Parse("#94A3B8"));
        private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(10);

        private readonly ReviewSnapshotService _reviewSnapshotService;
        private readonly GitService _gitService;
        private readonly DiffService _diffService;
        private readonly ReviewClipboardContextService _reviewClipboardContextService;
        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly AppCommonRuntime _appCommonRuntime;
        private readonly TransientEventService _transientEventService;
        private readonly DispatcherTimer _autoRefreshTimer;
        private ReviewClipboardClipLocateResult? _clipLocateResult;
        private IReadOnlyList<ReviewFilePath> _unreviewedFiles = Array.Empty<ReviewFilePath>();
        private IReadOnlyList<ReviewFilePath> _reviewedFiles = Array.Empty<ReviewFilePath>();
        private ReviewFilePath? _selectedUnreviewedFile;
        private ReviewFilePath? _selectedReviewedFile;
        private string _reviewPromptText = string.Empty;
        private string _clipLocateStatusText = "Unmatched";
        private IBrush _clipLocateButtonForeground = UnmatchedForeground;
        private bool _hasFrontSession;
        private bool _isDisposed;

        public ReviewViewModel(
            ReviewSnapshotService reviewSnapshotService,
            GitService gitService,
            DiffService diffService,
            ReviewClipboardContextService reviewClipboardContextService,
            SessionLifecycleService sessionLifecycleService,
            AppCommonRuntime appCommonRuntime)
        {
            _reviewSnapshotService = reviewSnapshotService
                ?? throw new ArgumentNullException(nameof(reviewSnapshotService));
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            _diffService = diffService ?? throw new ArgumentNullException(nameof(diffService));
            _reviewClipboardContextService = reviewClipboardContextService
                ?? throw new ArgumentNullException(nameof(reviewClipboardContextService));
            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            _appCommonRuntime = appCommonRuntime ?? throw new ArgumentNullException(nameof(appCommonRuntime));
            _transientEventService = _appCommonRuntime.TransientEventService;
            RefreshCommand = new AsyncRelayCommand(RefreshReviewFilesAsync);
            ModifiedGit = new GitViewModel(
                _gitService,
                _appCommonRuntime,
                RefreshCommand);
            ShowClipLocateLineCommand = new AsyncRelayCommand(ShowClipLocateResultAsync);
            ActivateFrontClientCommand = new RelayCommand(ActivateFrontClient, CanActivateFrontClient);
            SendPromptCommand = new RelayCommand(SendPrompt, CanSendPrompt);
            LaunchPromptCommand = new RelayCommand(LaunchPrompt, CanLaunchPrompt);
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
            _reviewClipboardContextService.ClipLocateChanged += OnClipLocateChanged;
            _sessionLifecycleService.SessionsChanged += RefreshFrontSessionState;
            WorkspaceRuntime.Changed += OnWorkspaceChanged;
            OnWorkspaceChanged(WorkspaceRuntime.Current);
            ApplyClipLocateResult(_reviewClipboardContextService.CurrentClipLocateResult);
            RefreshFrontSessionState();
            _autoRefreshTimer = new DispatcherTimer(
                AutoRefreshInterval,
                DispatcherPriority.Background,
                OnAutoRefreshTimerTick);
            _autoRefreshTimer.Start();
        }

        public IAsyncRelayCommand RefreshCommand { get; }

        public IAsyncRelayCommand ShowClipLocateLineCommand { get; }

        public IRelayCommand ActivateFrontClientCommand { get; }

        public IRelayCommand SendPromptCommand { get; }

        public IRelayCommand LaunchPromptCommand { get; }

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

        public string ReviewPromptText
        {
            get => _reviewPromptText;
            set
            {
                if (SetProperty(ref _reviewPromptText, value ?? string.Empty))
                {
                    SendPromptCommand.NotifyCanExecuteChanged();
                    LaunchPromptCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool HasFrontSession
        {
            get => _hasFrontSession;
            private set
            {
                if (SetProperty(ref _hasFrontSession, value))
                {
                    OnPropertyChanged(nameof(FrontSessionButtonForeground));
                    ActivateFrontClientCommand.NotifyCanExecuteChanged();
                    SendPromptCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public IBrush FrontSessionButtonForeground => HasFrontSession
            ? FrontSessionForeground
            : DisabledForeground;

        public string ClipLocateStatusText
        {
            get => _clipLocateStatusText;
            private set => SetProperty(ref _clipLocateStatusText, value);
        }

        public IBrush ClipLocateButtonForeground
        {
            get => _clipLocateButtonForeground;
            private set => SetProperty(ref _clipLocateButtonForeground, value);
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
            _reviewClipboardContextService.ClipLocateChanged -= OnClipLocateChanged;
            _sessionLifecycleService.SessionsChanged -= RefreshFrontSessionState;
        }

        public void OnSelected()
        {
            RequestRefreshReviewFiles();
        }

        private void ActivateFrontClient()
        {
            _sessionLifecycleService.ActivateFrontClient();
        }

        private bool CanActivateFrontClient()
        {
            return HasFrontSession;
        }

        private bool CanSendPrompt()
        {
            return HasFrontSession && HasReviewPromptText();
        }

        private bool CanLaunchPrompt()
        {
            return HasReviewPromptText();
        }

        private bool HasReviewPromptText()
        {
            return !string.IsNullOrWhiteSpace(ReviewPromptText);
        }

        private void RefreshFrontSessionState(object? sender = null, EventArgs? e = null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RefreshFrontSessionState());
                return;
            }

            if (!_isDisposed)
            {
                HasFrontSession = _sessionLifecycleService.HasFrontSession;
            }
        }

        private void OnClipLocateChanged(object? sender, ReviewClipboardClipLocateChangedEventArgs e)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplyClipLocateResult(e.Result);
                return;
            }

            Dispatcher.UIThread.Post(() => ApplyClipLocateResult(e.Result));
        }

        private void ApplyClipLocateResult(ReviewClipboardClipLocateResult? result)
        {
            if (_isDisposed)
            {
                return;
            }

            _clipLocateResult = result;
            var isMatched = result?.Status == ReviewClipboardClipLocateStatus.UniqueMatch
                && result.ClipLocate is not null;
            ClipLocateStatusText = isMatched ? "Matched" : "Unmatched";
            ClipLocateButtonForeground = isMatched ? MatchedForeground : UnmatchedForeground;
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
            ModifiedGit.ApplyWorkspaceConfig(workspace.EffectiveConfig);
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
                _diffService.OpenDiff(
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
                _diffService.OpenDiff(
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

        private async Task ShowClipLocateResultAsync()
        {
            var message = ReviewClipboardClipLocateResult.FormatResultText(_clipLocateResult);

            await _appCommonRuntime.PlatformInfrastructure.ShowMessageDialogAsync(
                "Review ClipLocate",
                message);
        }

        private void SendPrompt()
        {
            _sessionLifecycleService.SendMessageToFrontSession(ReviewPromptText);
        }

        private void LaunchPrompt()
        {
            var result = _sessionLifecycleService.Launch(ReviewPromptText);
            if (result.Started)
            {
                Log.Info("Review", result.StatusMessage);
                return;
            }

            _transientEventService.ReportUserActionFailure(
                "Review",
                "Launch failed",
                result.StatusMessage);
        }

        private sealed record ReviewFilesRefreshResult(
            IReadOnlyList<ReviewFilePath> UnreviewedFiles,
            IReadOnlyList<ReviewFilePath> ReviewedFiles);
    }
}
