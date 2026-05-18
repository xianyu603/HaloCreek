using HaloCreek.Models;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class OngoingSessionsViewModel : ViewModelBase
    {
        private WorkspaceInfo? _workspace;

        public WorkspaceInfo? Workspace
        {
            get => _workspace;
            private set => SetProperty(ref _workspace, value);
        }

        public void SetWorkspace(WorkspaceInfo workspace)
        {
            Workspace = workspace;
        }
    }
}
