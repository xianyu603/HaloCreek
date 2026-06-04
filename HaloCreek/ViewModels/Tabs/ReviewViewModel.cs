using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
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

        private readonly ReviewFileService _reviewFileService;
        private readonly ReviewClipboardContextService _reviewClipboardContextService;
        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly AppCommonRuntime _appCommonRuntime;
        private readonly TransientEventService _transientEventService;
        private ReviewPanelLayoutState _panelLayoutState;
        private ReviewClipboardClipLocateResult? _clipLocateResult;
        private IReadOnlyList<ReviewFilePath> _unreviewedFiles = Array.Empty<ReviewFilePath>();
        private ReviewFilePath? _selectedUnreviewedFile;
        private string _clipLocateStatusText = "Unmatched";
        private IBrush _clipLocateButtonForeground = UnmatchedForeground;
        private bool _hasFrontSession;
        private bool _isDisposed;

        public ReviewViewModel(
            ReviewFileService reviewFileService,
            WorkspaceRuntimeService workspaceRuntimeService,
            ReviewClipboardContextService reviewClipboardContextService,
            SessionLifecycleService sessionLifecycleService,
            AppCommonRuntime appCommonRuntime)
        {
            _reviewFileService = reviewFileService
                ?? throw new ArgumentNullException(nameof(reviewFileService));
            ArgumentNullException.ThrowIfNull(workspaceRuntimeService);
            _reviewClipboardContextService = reviewClipboardContextService
                ?? throw new ArgumentNullException(nameof(reviewClipboardContextService));
            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            _appCommonRuntime = appCommonRuntime ?? throw new ArgumentNullException(nameof(appCommonRuntime));
            _transientEventService = _appCommonRuntime.TransientEventService;
            MoveLeftCommand = new RelayCommand(MoveLeft, CanMoveLeft);
            MoveRightCommand = new RelayCommand(MoveRight, CanMoveRight);
            ShowClipLocateLineCommand = new AsyncRelayCommand(ShowClipLocateResultAsync);
            ActivateFrontClientCommand = new RelayCommand(ActivateFrontClient, CanActivateFrontClient);
            SendPromptCommand = new AsyncRelayCommand(SendPromptAsync, CanSendPrompt);
            _reviewClipboardContextService.ClipLocateChanged += OnClipLocateChanged;
            _sessionLifecycleService.SessionsChanged += RefreshFrontSessionState;
            workspaceRuntimeService.ApplyCurrentWorkspaceAndSubscribe(OnWorkspaceChanged);
            ApplyClipLocateResult(_reviewClipboardContextService.CurrentClipLocateResult);
            RefreshFrontSessionState();
        }

        public bool IsLeftPanelExpanded => _panelLayoutState != ReviewPanelLayoutState.LeftCollapsed;

        public bool IsRightPanelExpanded => _panelLayoutState != ReviewPanelLayoutState.RightCollapsed;

        public GridLength LeftPanelWidth => _panelLayoutState == ReviewPanelLayoutState.LeftCollapsed
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);

        public GridLength RightPanelWidth => _panelLayoutState == ReviewPanelLayoutState.RightCollapsed
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);

        public GridLength PanelGapWidth => IsLeftPanelExpanded && IsRightPanelExpanded
            ? new GridLength(12)
            : new GridLength(0);

        public IRelayCommand MoveLeftCommand { get; }

        public IRelayCommand MoveRightCommand { get; }

        public IAsyncRelayCommand ShowClipLocateLineCommand { get; }

        public IRelayCommand ActivateFrontClientCommand { get; }

        public IAsyncRelayCommand SendPromptCommand { get; }

        public IReadOnlyList<ReviewFilePath> UnreviewedFiles
        {
            get => _unreviewedFiles;
            private set => SetProperty(ref _unreviewedFiles, value);
        }

        public ReviewFilePath? SelectedUnreviewedFile
        {
            get => _selectedUnreviewedFile;
            set => SetProperty(ref _selectedUnreviewedFile, value);
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
            _reviewClipboardContextService.ClipLocateChanged -= OnClipLocateChanged;
            _sessionLifecycleService.SessionsChanged -= RefreshFrontSessionState;
        }

        public void OnSelected()
        {
            RefreshUnreviewedFiles();
        }

        private void MoveLeft()
        {
            PanelLayoutState = _panelLayoutState == ReviewPanelLayoutState.RightCollapsed
                ? ReviewPanelLayoutState.BothExpanded
                : ReviewPanelLayoutState.LeftCollapsed;
        }

        private bool CanMoveLeft()
        {
            return _panelLayoutState != ReviewPanelLayoutState.LeftCollapsed;
        }

        private void MoveRight()
        {
            PanelLayoutState = _panelLayoutState == ReviewPanelLayoutState.LeftCollapsed
                ? ReviewPanelLayoutState.BothExpanded
                : ReviewPanelLayoutState.RightCollapsed;
        }

        private bool CanMoveRight()
        {
            return _panelLayoutState != ReviewPanelLayoutState.RightCollapsed;
        }

        private ReviewPanelLayoutState PanelLayoutState
        {
            set
            {
                if (_panelLayoutState != value)
                {
                    _panelLayoutState = value;
                    OnPanelLayoutChanged();
                }
            }
        }

        private void OnPanelLayoutChanged()
        {
            OnPropertyChanged(nameof(IsLeftPanelExpanded));
            OnPropertyChanged(nameof(IsRightPanelExpanded));
            OnPropertyChanged(nameof(LeftPanelWidth));
            OnPropertyChanged(nameof(RightPanelWidth));
            OnPropertyChanged(nameof(PanelGapWidth));
            MoveLeftCommand.NotifyCanExecuteChanged();
            MoveRightCommand.NotifyCanExecuteChanged();
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

        private void RefreshUnreviewedFiles()
        {
            try
            {
                SelectedUnreviewedFile = null;
                UnreviewedFiles = _reviewFileService.GetUnreviewedFiles();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                _transientEventService.ReportUserActionFailure(
                    "Review",
                    "Review unreviewed files failed",
                    ex.Message,
                    ex);
            }
        }

        private void OnWorkspaceChanged(object? sender, WorkspaceRuntimeChangedEventArgs e)
        {
            RefreshUnreviewedFiles();
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

        private enum ReviewPanelLayoutState
        {
            BothExpanded,
            LeftCollapsed,
            RightCollapsed
        }
    }
}
