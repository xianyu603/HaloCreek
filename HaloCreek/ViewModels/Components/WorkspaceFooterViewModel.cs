using CommunityToolkit.Mvvm.Input;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Components
{
    public sealed class WorkspaceFooterViewModel : ViewModelBase
    {
        private readonly WorkspaceService _workspaceService;
        private string _workspacePath = "No workspace selected";
        private string _statusText = "Ready";

        public WorkspaceFooterViewModel()
            : this(new WorkspaceService())
        {
        }

        public WorkspaceFooterViewModel(WorkspaceService workspaceService)
        {
            _workspaceService = workspaceService;
            ChooseWorkspaceCommand = new RelayCommand(ChooseWorkspace);
        }

        public string WorkspacePath
        {
            get => _workspacePath;
            set => SetProperty(ref _workspacePath, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public IRelayCommand ChooseWorkspaceCommand { get; }

        public void SetWorkspace(WorkspaceInfo workspace)
        {
            WorkspacePath = workspace.Path;
        }

        private void ChooseWorkspace()
        {
            var workspace = _workspaceService.GetCurrentWorkspace();
            StatusText = workspace is null
                ? "Workspace selection is not connected yet"
                : $"Current workspace: {workspace.Path}";
        }
    }
}
