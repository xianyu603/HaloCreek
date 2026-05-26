// agent 开发平台

using System;
using HaloCreek.Models;
using HaloCreek.ViewModels.Components;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private const int PromptEditorTabIndex = 0;

        private string? _currentWorkspacePath;
        private int _selectedTabIndex;

        public MainWindowViewModel(
            string startupWorkspacePath,
            PromptEditorViewModel promptEditor,
            HistorySessionsViewModel historySessions,
            GitViewModel git,
            WorkspaceFooterViewModel workspaceFooter)
        {
            PromptEditor = promptEditor;
            HistorySessions = historySessions;
            Git = git;
            WorkspaceFooter = workspaceFooter;

            WorkspaceFooter.SetWorkspaceDispatcher(ApplyValidatedWorkspacePath);
            PromptEditor.SetStatusDispatcher(message => WorkspaceFooter.StatusText = message);
            HistorySessions.SetStatusDispatcher(message => WorkspaceFooter.StatusText = message);
            HistorySessions.SetReeditInitialPromptDispatcher(ReeditInitialPrompt);
            Git.SetStatusDispatcher(message => WorkspaceFooter.StatusText = message);
            ApplyValidatedWorkspacePath(startupWorkspacePath);
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
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public void ApplyValidatedWorkspacePath(string workspacePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            CurrentWorkspacePath = workspacePath;

            PromptEditor.SetWorkspacePath(workspacePath);
            HistorySessions.SetWorkspacePath(workspacePath);
            Git.SetWorkspacePath(workspacePath);
            WorkspaceFooter.SetWorkspacePath(workspacePath);
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
