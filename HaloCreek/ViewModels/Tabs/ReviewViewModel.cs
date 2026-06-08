using System;
using System.Collections.Generic;
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

        private readonly ReviewSnapshotService _reviewSnapshotService;
        private readonly GitService _gitService;
        private readonly DiffService _diffService;
        private readonly ReviewClipboardContextService _reviewClipboardContextService;
        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly AppCommonRuntime _appCommonRuntime;
        private readonly TransientEventService _transientEventService;
        private ReviewClipboardClipLocateResult? _clipLocateResult;
        private IReadOnlyList<ReviewFilePath> _unreviewedFiles = Array.Empty<ReviewFilePath>();
        private IReadOnlyList<ReviewFilePath> _reviewedFiles = Array.Empty<ReviewFilePath>();
        private ReviewFilePath? _selectedUnreviewedFile;
        private ReviewFilePath? _selectedReviewedFile;
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
            SendPromptCommand = new AsyncRelayCommand(SendPromptAsync, CanSendPrompt);
            AddReviewedCommand = new RelayCommand<ReviewFilePath>(AddReviewed);
            MarkUnreviewedCommand = new RelayCommand<ReviewFilePath>(MarkUnreviewed);
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
        }

        public IAsyncRelayCommand RefreshCommand { get; }

        public IAsyncRelayCommand ShowClipLocateLineCommand { get; }

        public IRelayCommand ActivateFrontClientCommand { get; }

        public IAsyncRelayCommand SendPromptCommand { get; }

        public IRelayCommand<ReviewFilePath> AddReviewedCommand { get; }

        public IRelayCommand<ReviewFilePath> MarkUnreviewedCommand { get; }

        public IRelayCommand<ReviewFilePath> DiffUnreviewedAgainstReviewedCommand { get; }

        public IRelayCommand<ReviewFilePath> DiffReviewedAgainstWorkingTreeCommand { get; }

        public IRelayCommand<ReviewFilePath> DiffReviewedAgainstHeadCommand { get; }

        public GitViewModel ModifiedGit { get; }

        public IReadOnlyList<ReviewFilePath> UnreviewedFiles
        {
            get => _unreviewedFiles;
            private set => SetProperty(ref _unreviewedFiles, value);
        }

        public IReadOnlyList<ReviewFilePath> ReviewedFiles
        {
            get => _reviewedFiles;
            private set => SetProperty(ref _reviewedFiles, value);
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
            return HasFrontSession;
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

                SelectedUnreviewedFile = null;
                SelectedReviewedFile = null;
                UnreviewedFiles = result.UnreviewedFiles;
                ReviewedFiles = result.ReviewedFiles;
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

        private async Task SendPromptAsync()
        {
            var initialText = _clipLocateResult?.Status == ReviewClipboardClipLocateStatus.UniqueMatch
                && _clipLocateResult.ClipLocate is not null
                    ? _clipLocateResult.ClipLocate.FormatLocationText()
                        + Environment.NewLine
                    : string.Empty;

            var message = await _appCommonRuntime.PlatformInfrastructure.ShowTextInputDialogAsync(
                "Send Prompt",
                initialText,
                "Send",
                "Cancel");

            if (message is null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _sessionLifecycleService.SendMessageToFrontSession(message);
        }

        private sealed record ReviewFilesRefreshResult(
            IReadOnlyList<ReviewFilePath> UnreviewedFiles,
            IReadOnlyList<ReviewFilePath> ReviewedFiles);
    }
}
