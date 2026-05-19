using CommunityToolkit.Mvvm.Input;

namespace HaloCreek.ViewModels.Components
{
    public sealed class WorkspaceFooterViewModel : ViewModelBase
    {
        private string _workspacePath = "No workspace selected";
        private string _statusText = "Ready";

        public WorkspaceFooterViewModel()
        {
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

        public void SetWorkspacePath(string workspacePath)
        {
            WorkspacePath = workspacePath;
        }

        private void ChooseWorkspace()
        {
            StatusText = "Workspace selection is not connected yet";
        }
    }
}
