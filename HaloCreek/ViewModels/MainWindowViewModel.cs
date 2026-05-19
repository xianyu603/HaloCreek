// agent 开发平台

using System;
using HaloCreek.Infrastructure;
using HaloCreek.ViewModels.Components;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private const string InvalidWorkspacePathStatusText = "Invalid workspace path";
        private const string ReadyStatusText = "Ready";

        private readonly PlatformInfrastructure _platformInfrastructure;
        private string? _currentWorkspacePath;

        public MainWindowViewModel(
            PlatformInfrastructure platformInfrastructure,
            PromptEditorViewModel promptEditor,
            HistorySessionsViewModel historySessions,
            OngoingSessionsViewModel ongoingSessions,
            GitViewModel git,
            WorkspaceFooterViewModel workspaceFooter)
        {
            _platformInfrastructure = platformInfrastructure ?? throw new ArgumentNullException(nameof(platformInfrastructure));
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

        public void SetWorkspacePath(string? workspacePath)
        {
            if (!_platformInfrastructure.TryNormalizeValidDirectoryPath(workspacePath, out var normalizedPath))
            {
                if (!string.IsNullOrWhiteSpace(workspacePath))
                {
                    WorkspaceFooter.StatusText = InvalidWorkspacePathStatusText;
                }

                return;
            }

            CurrentWorkspacePath = normalizedPath;

            PromptEditor.SetWorkspacePath(normalizedPath);
            HistorySessions.SetWorkspacePath(normalizedPath);
            OngoingSessions.SetWorkspacePath(normalizedPath);
            Git.SetWorkspacePath(normalizedPath);
            WorkspaceFooter.SetWorkspacePath(normalizedPath);
            WorkspaceFooter.StatusText = ReadyStatusText;
        }
    }
}
