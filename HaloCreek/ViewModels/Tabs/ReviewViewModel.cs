using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class ReviewViewModel : ViewModelBase
    {
        private ReviewPanelLayoutState _panelLayoutState;

        public ReviewViewModel()
        {
            MoveLeftCommand = new RelayCommand(MoveLeft, CanMoveLeft);
            MoveRightCommand = new RelayCommand(MoveRight, CanMoveRight);
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

        private enum ReviewPanelLayoutState
        {
            BothExpanded,
            LeftCollapsed,
            RightCollapsed
        }
    }
}
