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
        private string? _workspacePath;

        public GitViewModel()
            : this(new GitService())
        {
        }

        public GitViewModel(GitService gitService)
        {
            _gitService = gitService;
        }

        public string? WorkspacePath
        {
            get => _workspacePath;
            private set => SetProperty(ref _workspacePath, value);
        }

        public IReadOnlyList<GitChangeInfo> Changes
        {
            get => _changes;
            private set => SetProperty(ref _changes, value);
        }

        public void SetWorkspacePath(string workspacePath)
        {
            WorkspacePath = workspacePath;
            RefreshChanges();
        }

        public void RefreshChanges()
        {
            Changes = _gitService.GetChanges(WorkspacePath);
        }
    }
}
