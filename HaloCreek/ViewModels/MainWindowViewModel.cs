// agent 开发平台

using System;
using System.Collections.Generic;
using HaloCreek.Models;
using HaloCreek.ViewModels.Components;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private const int PromptEditorTabIndex = 0;

        private readonly IReadOnlyList<ViewModelBase> _tabViewModels;
        private int _selectedTabIndex;

        public MainWindowViewModel(
            PromptEditorViewModel promptEditor,
            HistorySessionsViewModel historySessions,
            GitViewModel git,
            WorkspaceFooterViewModel workspaceFooter)
        {
            PromptEditor = promptEditor;
            HistorySessions = historySessions;
            Git = git;
            WorkspaceFooter = workspaceFooter;
            _tabViewModels = new ViewModelBase[] { PromptEditor, HistorySessions, Git };

            HistorySessions.SetReeditInitialPromptDispatcher(ReeditInitialPrompt);
        }

        public PromptEditorViewModel PromptEditor { get; }

        public HistorySessionsViewModel HistorySessions { get; }

        public GitViewModel Git { get; }

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

        private void ReeditInitialPrompt(HistorySessionInfo session)
        {
            ArgumentNullException.ThrowIfNull(session);

            if (string.IsNullOrWhiteSpace(session.InitialPrompt))
            {
                throw new InvalidOperationException("Initial prompt is empty.");
            }

            PromptEditor.PromptText = session.InitialPrompt;
            SelectedTabIndex = PromptEditorTabIndex;
        }
    }
}
