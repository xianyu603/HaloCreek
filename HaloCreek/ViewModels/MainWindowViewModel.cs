// agent 开发平台

using System;
using System.Collections.Generic;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.ViewModels.Components;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private const int PromptEditorTabIndex = 0;

        private readonly WorkspaceCacheService _workspaceCacheService;
        private readonly IReadOnlyList<ViewModelBase> _tabViewModels;
        private string? _currentWorkspacePath;
        private int _selectedTabIndex;

        public MainWindowViewModel(
            string startupWorkspacePath,
            WorkspaceCacheService workspaceCacheService,
            PromptEditorViewModel promptEditor,
            HistorySessionsViewModel historySessions,
            GitViewModel git,
            WorkspaceFooterViewModel workspaceFooter)
        {
            _workspaceCacheService = workspaceCacheService ?? throw new ArgumentNullException(nameof(workspaceCacheService));
            PromptEditor = promptEditor;
            HistorySessions = historySessions;
            Git = git;
            WorkspaceFooter = workspaceFooter;
            _tabViewModels = new ViewModelBase[] { PromptEditor, HistorySessions, Git };

            WorkspaceFooter.SetWorkspaceDispatcher(ApplyValidatedWorkspacePath);
            PromptEditor.SetStatusDispatcher(message => WorkspaceFooter.StatusText = message);
            HistorySessions.SetStatusDispatcher(message => WorkspaceFooter.StatusText = message);
            HistorySessions.SetReeditInitialPromptDispatcher(ReeditInitialPrompt);
            Git.SetStatusDispatcher(message => WorkspaceFooter.StatusText = message);
            SetWorkspacePath(startupWorkspacePath, cacheWorkspace: false);
        }

        public PromptEditorViewModel PromptEditor { get; }

        public HistorySessionsViewModel HistorySessions { get; }

        public GitViewModel Git { get; }

        public WorkspaceFooterViewModel WorkspaceFooter { get; }

        public string? CurrentWorkspacePath
        {
            get => _currentWorkspacePath;
            private set => SetProperty(ref _currentWorkspacePath, value);
        }

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

        public void ApplyValidatedWorkspacePath(string workspacePath)
        {
            SetWorkspacePath(workspacePath, cacheWorkspace: true);
        }

        private void SetWorkspacePath(string workspacePath, bool cacheWorkspace)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            CurrentWorkspacePath = workspacePath;

            PromptEditor.SetWorkspacePath(workspacePath);
            HistorySessions.SetWorkspacePath(workspacePath);
            Git.SetWorkspacePath(workspacePath);
            WorkspaceFooter.SetWorkspacePath(workspacePath);

            if (cacheWorkspace)
            {
                _workspaceCacheService.TrySaveLastWorkspacePath(workspacePath);
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
            WorkspaceFooter.StatusText = "Initial prompt copied to Prompt Editor.";
        }
    }
}
