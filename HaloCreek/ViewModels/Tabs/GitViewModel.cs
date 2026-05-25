using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class GitViewModel : ViewModelBase
    {
        private const string InitialEmptyStateText = "Select a workspace to view Git changes";

        private readonly GitService _gitService;
        private IReadOnlyList<GitChangeInfo> _changes = Array.Empty<GitChangeInfo>();
        private GitChangeInfo? _selectedChange;
        private string _emptyStateText = InitialEmptyStateText;
        private string? _workspacePath;
        private Action<string>? _statusDispatcher;

        public GitViewModel()
            : this(new GitService())
        {
        }

        public GitViewModel(GitService gitService)
        {
            _gitService = gitService;
            RefreshCommand = new RelayCommand(RefreshChanges, () => HasWorkspace);
        }

        public string? WorkspacePath
        {
            get => _workspacePath;
            private set
            {
                if (SetProperty(ref _workspacePath, value))
                {
                    OnPropertyChanged(nameof(HasWorkspace));
                    RefreshCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public IReadOnlyList<GitChangeInfo> Changes
        {
            get => _changes;
            private set
            {
                if (SetProperty(ref _changes, value))
                {
                    OnPropertyChanged(nameof(HasChanges));
                    OnPropertyChanged(nameof(IsEmptyStateVisible));
                }
            }
        }

        public GitChangeInfo? SelectedChange
        {
            get => _selectedChange;
            set => SetProperty(ref _selectedChange, value);
        }

        public string EmptyStateText
        {
            get => _emptyStateText;
            private set => SetProperty(ref _emptyStateText, value);
        }

        public bool HasWorkspace => !string.IsNullOrWhiteSpace(WorkspacePath);

        public bool HasChanges => Changes.Count > 0;

        public bool IsEmptyStateVisible => !HasChanges;

        public IRelayCommand RefreshCommand { get; }

        public void SetWorkspacePath(string workspacePath)
        {
            WorkspacePath = workspacePath;
            RefreshChanges();
        }

        public void SetStatusDispatcher(Action<string> statusDispatcher)
        {
            _statusDispatcher = statusDispatcher ?? throw new ArgumentNullException(nameof(statusDispatcher));
        }

        public void RefreshChanges()
        {
            var result = _gitService.GetChanges(WorkspacePath);

            SelectedChange = null;
            Changes = result.Changes;
            EmptyStateText = result.Changes.Count == 0
                ? result.Message
                : string.Empty;
            _statusDispatcher?.Invoke(result.Message);
        }
    }
}
