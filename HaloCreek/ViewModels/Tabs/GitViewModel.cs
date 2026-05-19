using System;
using System.Collections.Generic;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class GitViewModel : ViewModelBase
    {
        private readonly GitService _gitService;
        private IReadOnlyList<GitChangeInfo> _changes = Array.Empty<GitChangeInfo>();
        private WorkspaceInfo? _workspace;

        public GitViewModel()
            : this(new GitService())
        {
        }

        public GitViewModel(GitService gitService)
        {
            _gitService = gitService;
        }

        public WorkspaceInfo? Workspace
        {
            get => _workspace;
            private set => SetProperty(ref _workspace, value);
        }

        public IReadOnlyList<GitChangeInfo> Changes
        {
            get => _changes;
            private set => SetProperty(ref _changes, value);
        }

        public void SetWorkspace(WorkspaceInfo workspace)
        {
            Workspace = workspace;
            RefreshChanges();
        }

        public void RefreshChanges()
        {
            Changes = _gitService.GetChanges(Workspace);
        }
    }
}
