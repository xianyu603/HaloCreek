using HaloCreek.Models;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class HistorySessionsViewModel : ViewModelBase
    {
        private string _searchText = string.Empty;
        private WorkspaceInfo? _workspace;

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

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
