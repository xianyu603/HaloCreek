// agent 开发平台

using System;
using HaloCreek.ViewModels.Components;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private string? _currentWorkspacePath;

        public MainWindowViewModel(
            PromptEditorViewModel promptEditor,
            HistorySessionsViewModel historySessions,
            OngoingSessionsViewModel ongoingSessions,
            GitViewModel git,
            WorkspaceFooterViewModel workspaceFooter)
        {
            PromptEditor = promptEditor;
            HistorySessions = historySessions;
            OngoingSessions = ongoingSessions;
            Git = git;
            WorkspaceFooter = workspaceFooter;

            WorkspaceFooter.SetWorkspaceDispatcher(SetWorkspacePath);
            PromptEditor.SetStatusDispatcher(message => WorkspaceFooter.StatusText = message);
        }

        public PromptEditorViewModel PromptEditor { get; }

        public HistorySessionsViewModel HistorySessions { get; }

        public OngoingSessionsViewModel OngoingSessions { get; }

        public GitViewModel Git { get; }

        public WorkspaceFooterViewModel WorkspaceFooter { get; }

        public string? CurrentWorkspacePath
        {
            get => _currentWorkspacePath;
            private set => SetProperty(ref _currentWorkspacePath, value);
        }

        public void SetWorkspacePath(string workspacePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            CurrentWorkspacePath = workspacePath;

            PromptEditor.SetWorkspacePath(workspacePath);
            HistorySessions.SetWorkspacePath(workspacePath);
            OngoingSessions.SetWorkspacePath(workspacePath);
            Git.SetWorkspacePath(workspacePath);
            WorkspaceFooter.SetWorkspacePath(workspacePath);
        }
    }
}
