// agent 开发平台

using HaloCreek.ViewModels.Components;

namespace HaloCreek.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
            : this(new WorkspaceFooterViewModel())
        {
        }

        public MainWindowViewModel(WorkspaceFooterViewModel workspaceFooter)
        {
            WorkspaceFooter = workspaceFooter;
        }

        public WorkspaceFooterViewModel WorkspaceFooter { get; }
    }
}
