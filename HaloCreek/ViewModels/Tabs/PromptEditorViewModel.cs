using HaloCreek.Models;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class PromptEditorViewModel : ViewModelBase
    {
        private string _promptText = string.Empty;
        private WorkspaceInfo? _workspace;

        public string PromptText
        {
            get => _promptText;
            set => SetProperty(ref _promptText, value);
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
