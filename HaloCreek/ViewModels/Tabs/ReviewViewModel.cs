using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class ReviewViewModel : ViewModelBase, IDisposable
    {
        private static readonly IBrush MatchedForeground = new SolidColorBrush(Color.Parse("#166534"));
        private static readonly IBrush UnmatchedForeground = new SolidColorBrush(Color.Parse("#854D0E"));

        private readonly ReviewClipboardContextService _reviewClipboardContextService;
        private readonly AppCommonRuntime _appCommonRuntime;
        private ReviewPanelLayoutState _panelLayoutState;
        private ReviewClipboardClipLocateResult? _clipLocateResult;
        private string _clipLocateStatusText = "Unmatched";
        private IBrush _clipLocateButtonForeground = UnmatchedForeground;
        private bool _isDisposed;

        public ReviewViewModel(
            ReviewClipboardContextService reviewClipboardContextService,
            AppCommonRuntime appCommonRuntime)
        {
            _reviewClipboardContextService = reviewClipboardContextService
                ?? throw new ArgumentNullException(nameof(reviewClipboardContextService));
            _appCommonRuntime = appCommonRuntime ?? throw new ArgumentNullException(nameof(appCommonRuntime));
            MoveLeftCommand = new RelayCommand(MoveLeft, CanMoveLeft);
            MoveRightCommand = new RelayCommand(MoveRight, CanMoveRight);
            ShowClipLocateLineCommand = new AsyncRelayCommand(ShowClipLocateResultAsync);
            _reviewClipboardContextService.ClipLocateChanged += OnClipLocateChanged;
            ApplyClipLocateResult(_reviewClipboardContextService.CurrentClipLocateResult);
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

        private async Task ShowClipLocateResultAsync()
        {
            var result = _clipLocateResult;
            var clipLocate = result?.ClipLocate;
            string message;
            if (clipLocate is not null)
            {
                var lineRange = clipLocate.StartLine == clipLocate.EndLine
                    ? $"Line {clipLocate.StartLine}"
                    : $"Lines {clipLocate.StartLine}-{clipLocate.EndLine}";
                message =
                    "Matched"
                    + Environment.NewLine
                    + $"Path: {clipLocate.RelativePath}"
                    + Environment.NewLine
                    + $"{lineRange}, Columns {clipLocate.StartColumn}-{clipLocate.EndColumn}";
            }
            else
            {
                var failureReason = string.IsNullOrWhiteSpace(result?.FailureReason)
                    ? "Unknown"
                    : result.FailureReason;
                var detail = string.IsNullOrWhiteSpace(result?.Message)
                    ? "No clipboard clip locate result is available."
                    : result.Message;
                message =
                    "Unmatched"
                    + Environment.NewLine
                    + $"Failure reason: {failureReason}"
                    + Environment.NewLine
                    + detail;
            }

            await _appCommonRuntime.PlatformInfrastructure.ShowMessageDialogAsync(
                "Review ClipLocate",
                message);
        }

        private enum ReviewPanelLayoutState
        {
            BothExpanded,
            LeftCollapsed,
            RightCollapsed
        }
    }
}
