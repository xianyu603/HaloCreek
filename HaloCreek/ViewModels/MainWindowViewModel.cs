// agent 开发平台

using HaloCreek.ViewModels.Components;
using HaloCreek.ViewModels.Tabs;

namespace HaloCreek.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
            : this(
                new PromptEditorViewModel(),
                new HistorySessionsViewModel(),
                new OngoingSessionsViewModel(),
                new GitViewModel(),
                new WorkspaceFooterViewModel())
        {
        }

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
        }

        public PromptEditorViewModel PromptEditor { get; }

        public HistorySessionsViewModel HistorySessions { get; }

        public OngoingSessionsViewModel OngoingSessions { get; }

        public GitViewModel Git { get; }

        public WorkspaceFooterViewModel WorkspaceFooter { get; }
    }
}
