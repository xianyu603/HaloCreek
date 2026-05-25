using HaloCreek.Models;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class GitFileActionButtonViewModel
    {
        public GitFileActionButtonViewModel(GitFileBrowserActionConfig action)
        {
            Action = action;
            Label = string.IsNullOrWhiteSpace(action.Label)
                ? action.Id
                : action.Label;
        }

        public GitFileBrowserActionConfig Action { get; }

        public string Label { get; }
    }
}
