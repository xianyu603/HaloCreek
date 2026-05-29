using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class GitViewModel : ViewModelBase, ITabSelectionAware
    {
        private const string InitialEmptyStateText = "Select a workspace to view Git changes";

        private readonly GitService _gitService;
        private readonly TransientEventService _transientEventService;
        private IReadOnlyList<GitChangeInfo> _changes = Array.Empty<GitChangeInfo>();
        private IReadOnlyList<GitFileActionButtonViewModel> _leftActionButtons = Array.Empty<GitFileActionButtonViewModel>();
        private IReadOnlyList<GitFileActionButtonViewModel> _rightActionButtons = Array.Empty<GitFileActionButtonViewModel>();
        private GitChangeInfo? _selectedChange;
        private GitFileBrowserActionConfig? _doubleClickAction;
        private string _doubleClickActionId = string.Empty;
        private string _emptyStateText = InitialEmptyStateText;
        private string? _workspacePath;

        public GitViewModel(
            GitService gitService,
            WorkspaceRuntimeService workspaceRuntimeService,
            AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            ArgumentNullException.ThrowIfNull(workspaceRuntimeService);
            _transientEventService = appCommonRuntime.TransientEventService;
            RefreshCommand = new RelayCommand(RefreshChanges, () => HasWorkspace);
            RunActionCommand = new RelayCommand<GitFileActionButtonViewModel>(RunAction, CanRunAction);
            OpenSelectedChangeCommand = new RelayCommand<GitChangeInfo>(OpenSelectedChange, CanOpenSelectedChange);
            workspaceRuntimeService.ApplyCurrentWorkspaceAndSubscribe(OnWorkspaceChanged);
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

        private void ApplyWorkspacePath(string workspacePath, AppConfig config)
        {
            WorkspacePath = workspacePath;
            LoadActionConfig(config);
            RefreshChanges();
        }

        public void OnSelected()
        {
            RefreshChanges();
        }

        public void RefreshChanges()
        {
            var result = _gitService.GetChanges(WorkspacePath);

            SelectedChange = null;
            Changes = result.Changes;
            EmptyStateText = result.Changes.Count == 0
                ? result.Message
                : string.Empty;
            Log.Info("Git", result.Message);
        }

        private void LoadActionConfig(AppConfig config)
        {
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

            RunConfiguredAction(button.Action, SelectedChange);
        }

        private void OpenSelectedChange(GitChangeInfo? change)
        {
            if (change is null)
            {
                return;
            }

            if (_doubleClickAction is null)
            {
                _transientEventService.ReportUserActionFailure(
                    "Git",
                    "Open failed",
                    $"Double click action not found: {_doubleClickActionId}");
                return;
            }

            RunConfiguredAction(_doubleClickAction, change);
        }

        private void RunConfiguredAction(GitFileBrowserActionConfig action, GitChangeInfo? selectedChange)
        {
            var result = _gitService.TryRunConfiguredAction(WorkspacePath, selectedChange, action);
            if (result.Succeeded)
            {
                Log.Info("Git", result.Message);
                return;
            }

            _transientEventService.ReportUserActionFailure(
                "Git",
                "Git action failed",
                result.Message);
        }

        private void OnWorkspaceChanged(object? sender, WorkspaceRuntimeChangedEventArgs e)
        {
            ApplyWorkspacePath(e.WorkspacePath, e.EffectiveConfig);
        }
    }
}
