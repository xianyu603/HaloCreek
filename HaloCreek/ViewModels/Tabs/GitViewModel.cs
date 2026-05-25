using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class GitViewModel : ViewModelBase
    {
        private const string InitialEmptyStateText = "Select a workspace to view Git changes";

        private readonly GitService _gitService;
        private readonly ConfigService _configService;
        private IReadOnlyList<GitChangeInfo> _changes = Array.Empty<GitChangeInfo>();
        private IReadOnlyList<GitFileActionButtonViewModel> _leftActionButtons = Array.Empty<GitFileActionButtonViewModel>();
        private IReadOnlyList<GitFileActionButtonViewModel> _rightActionButtons = Array.Empty<GitFileActionButtonViewModel>();
        private GitChangeInfo? _selectedChange;
        private GitFileBrowserActionConfig? _doubleClickAction;
        private string _doubleClickActionId = string.Empty;
        private string _emptyStateText = InitialEmptyStateText;
        private string? _workspacePath;
        private Action<string>? _statusDispatcher;

        public GitViewModel()
            : this(new GitService(), new ConfigService())
        {
        }

        public GitViewModel(
            GitService gitService,
            ConfigService configService)
        {
            _gitService = gitService;
            _configService = configService;
            RefreshCommand = new RelayCommand(RefreshChanges, () => HasWorkspace);
            RunActionCommand = new RelayCommand<GitFileActionButtonViewModel>(RunAction, CanRunAction);
            OpenSelectedChangeCommand = new RelayCommand<GitChangeInfo>(OpenSelectedChange, CanOpenSelectedChange);
            LoadActionConfig();
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
            set
            {
                if (SetProperty(ref _selectedChange, value))
                {
                    RunActionCommand.NotifyCanExecuteChanged();
                    OpenSelectedChangeCommand.NotifyCanExecuteChanged();
                }
            }
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

        public IReadOnlyList<GitFileActionButtonViewModel> LeftActionButtons
        {
            get => _leftActionButtons;
            private set => SetProperty(ref _leftActionButtons, value);
        }

        public IReadOnlyList<GitFileActionButtonViewModel> RightActionButtons
        {
            get => _rightActionButtons;
            private set => SetProperty(ref _rightActionButtons, value);
        }

        public IRelayCommand<GitFileActionButtonViewModel> RunActionCommand { get; }

        public IRelayCommand<GitChangeInfo> OpenSelectedChangeCommand { get; }

        public void SetWorkspacePath(string workspacePath)
        {
            WorkspacePath = workspacePath;
            LoadActionConfig();
            RefreshChanges();
        }

        public void SetStatusDispatcher(Action<string> statusDispatcher)
        {
            _statusDispatcher = statusDispatcher ?? throw new ArgumentNullException(nameof(statusDispatcher));
        }

        public void RefreshChanges()
        {
            LoadActionConfig();
            var result = _gitService.GetChanges(WorkspacePath);

            SelectedChange = null;
            Changes = result.Changes;
            EmptyStateText = result.Changes.Count == 0
                ? result.Message
                : string.Empty;
            _statusDispatcher?.Invoke(result.Message);
        }

        private void LoadActionConfig()
        {
            var config = _configService.LoadEffectiveConfig(WorkspacePath);
            _doubleClickActionId = config.GitFileBrowserDoubleClickActionId;
            _doubleClickAction = config.GitFileBrowserActions.FirstOrDefault(
                configuredAction => string.Equals(
                    configuredAction.Id,
                    _doubleClickActionId,
                    StringComparison.OrdinalIgnoreCase));
            LeftActionButtons = BuildActionButtons(config.GitFileBrowserActions, GitFileBrowserActionPlacement.Left);
            RightActionButtons = BuildActionButtons(config.GitFileBrowserActions, GitFileBrowserActionPlacement.Right);
            RunActionCommand.NotifyCanExecuteChanged();
            OpenSelectedChangeCommand.NotifyCanExecuteChanged();
        }

        private static IReadOnlyList<GitFileActionButtonViewModel> BuildActionButtons(
            IReadOnlyList<GitFileBrowserActionConfig> actions,
            GitFileBrowserActionPlacement placement)
        {
            return actions
                .Where(action => action.ShowAsButton && action.Placement == placement)
                .Select(action => new GitFileActionButtonViewModel(action))
                .ToArray();
        }

        private bool CanRunAction(GitFileActionButtonViewModel? button)
        {
            return button is not null
                && HasWorkspace
                && (!button.Action.RequiresSelectedChange || SelectedChange is not null);
        }

        private bool CanOpenSelectedChange(GitChangeInfo? change)
        {
            return HasWorkspace && change is not null;
        }

        private void RunAction(GitFileActionButtonViewModel? button)
        {
            if (button is null)
            {
                return;
            }

            ReportConfiguredActionSelected(button.Action);
        }

        private void OpenSelectedChange(GitChangeInfo? change)
        {
            if (change is null)
            {
                return;
            }

            if (_doubleClickAction is null)
            {
                _statusDispatcher?.Invoke($"Double click action not found: {_doubleClickActionId}");
                return;
            }

            ReportConfiguredActionSelected(_doubleClickAction);
        }

        private void ReportConfiguredActionSelected(GitFileBrowserActionConfig action)
        {
            _statusDispatcher?.Invoke($"Action selected: {action.Id}. Execution will be implemented in 6-T04.");
        }
    }
}
