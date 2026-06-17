using System.Collections.Generic;
using HaloCreek.ViewModels.Components;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly IReadOnlyList<ViewModelBase> _tabViewModels;
        private int _selectedTabIndex;

        public MainWindowViewModel(
            PromptEditorViewModel promptEditor,
            ReviewViewModel review,
            HistorySessionsViewModel historySessions,
            LogPanelViewModel logs,
            WorkspaceFooterViewModel workspaceFooter)
        {
            PromptEditor = promptEditor;
            Review = review;
            HistorySessions = historySessions;
            Logs = logs;
            WorkspaceFooter = workspaceFooter;
            _tabViewModels = new ViewModelBase[] { PromptEditor, Review, HistorySessions, Logs };
        }

        public PromptEditorViewModel PromptEditor { get; }

        public ReviewViewModel Review { get; }

        public HistorySessionsViewModel HistorySessions { get; }

        public LogPanelViewModel Logs { get; }

        public WorkspaceFooterViewModel WorkspaceFooter { get; }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (SetProperty(ref _selectedTabIndex, value)
                    && value >= 0
                    && value < _tabViewModels.Count
                    && _tabViewModels[value] is ITabSelectionAware selectedTab)
                {
                    selectedTab.OnSelected();
                }
            }
        }
    }
}
