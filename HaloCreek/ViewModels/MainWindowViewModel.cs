// agent 开发平台

using HaloCreek.ViewModels.Components;
using HaloCreek.ViewModels.Tabs;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly WorkspaceService _workspaceService;

        public MainWindowViewModel()
            : this(
                new WorkspaceService(),
                new PromptEditorViewModel(),
                new HistorySessionsViewModel(),
                new OngoingSessionsViewModel(),
                new GitViewModel(),
                new WorkspaceFooterViewModel())
        {
        }

        public MainWindowViewModel(
            WorkspaceService workspaceService,
            PromptEditorViewModel promptEditor,
            HistorySessionsViewModel historySessions,
            OngoingSessionsViewModel ongoingSessions,
            GitViewModel git,
            WorkspaceFooterViewModel workspaceFooter)
        {
            _workspaceService = workspaceService;
            PromptEditor = promptEditor;
            HistorySessions = historySessions;
            OngoingSessions = ongoingSessions;
            Git = git;
            WorkspaceFooter = workspaceFooter;
        }

        public PromptEditorViewModel PromptEditor { get; }

        public HistorySessionsViewModel HistorySessions { get; }

        public OngoingSessionsViewModel OngoingSessions { get; }

        public GitViewModel Git { get; }

        public WorkspaceFooterViewModel WorkspaceFooter { get; }

        public WorkspaceInfo? CurrentWorkspace => _workspaceService.GetCurrentWorkspace();

        public void SetWorkspace(WorkspaceInfo workspace)
        {
            _workspaceService.SetCurrentWorkspace(workspace);

            PromptEditor.SetWorkspace(workspace);
            HistorySessions.SetWorkspace(workspace);
            OngoingSessions.SetWorkspace(workspace);
            Git.SetWorkspace(workspace);
            WorkspaceFooter.SetWorkspace(workspace);
        }
    }
}
