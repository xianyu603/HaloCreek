using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class GitViewModel : ViewModelBase
    {
        private const string InitialEmptyStateText = "Select a workspace to view Git changes";

        private readonly GitService _gitService;
        private readonly TransientEventService _transientEventService;
        private IReadOnlyList<GitChangeInfo> _changes = Array.Empty<GitChangeInfo>();
        private IReadOnlyList<GitFileActionViewModel> _selectedFilePathActions = Array.Empty<GitFileActionViewModel>();
        private IReadOnlyList<GitFileActionViewModel> _workspaceRootActions = Array.Empty<GitFileActionViewModel>();
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
            RunActionCommand = new RelayCommand<GitFileActionViewModel>(RunAction, CanRunAction);
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

        public IReadOnlyList<GitFileActionViewModel> SelectedFilePathActions
        {
            get => _selectedFilePathActions;
            private set => SetProperty(ref _selectedFilePathActions, value);
        }

        public IReadOnlyList<GitFileActionViewModel> WorkspaceRootActions
        {
            get => _workspaceRootActions;
            private set => SetProperty(ref _workspaceRootActions, value);
        }

        public IRelayCommand<GitFileActionViewModel> RunActionCommand { get; }

        public IRelayCommand<GitChangeInfo> OpenSelectedChangeCommand { get; }

        private void ApplyWorkspacePath(string workspacePath, AppConfig config)
        {
            WorkspacePath = workspacePath;
            LoadActionConfig(config);
            RefreshChanges();
        }

        public void RefreshChanges()
        {
            var result = _gitService.GetChanges();

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
            SelectedFilePathActions = BuildActions(
                config.GitFileBrowserActions,
                GitFileBrowserActionTarget.SelectedFilePath);
            WorkspaceRootActions = BuildActions(
                config.GitFileBrowserActions,
                GitFileBrowserActionTarget.WorkspaceRoot);
            RunActionCommand.NotifyCanExecuteChanged();
            OpenSelectedChangeCommand.NotifyCanExecuteChanged();
        }

        private static IReadOnlyList<GitFileActionViewModel> BuildActions(
            IReadOnlyList<GitFileBrowserActionConfig> actions,
            GitFileBrowserActionTarget target)
        {
            return actions
                .Where(action => action.Target == target)
                .Select(action => new GitFileActionViewModel(action))
                .ToArray();
        }

        private bool CanRunAction(GitFileActionViewModel? action)
        {
            return action is not null
                && HasWorkspace
                && (!action.Action.RequiresSelectedChange || SelectedChange is not null);
        }

        private bool CanOpenSelectedChange(GitChangeInfo? change)
        {
            return HasWorkspace && change is not null;
        }

        private void RunAction(GitFileActionViewModel? action)
        {
            if (action is null)
            {
                return;
            }

            RunConfiguredAction(action.Action, SelectedChange);
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

            if (_doubleClickAction.Target != GitFileBrowserActionTarget.SelectedFilePath)
            {
                _transientEventService.ReportUserActionFailure(
                    "Git",
                    "Open failed",
                    $"Double click action must target {GitFileBrowserActionTarget.SelectedFilePath}: {_doubleClickActionId}");
                return;
            }

            RunConfiguredAction(_doubleClickAction, change);
        }

        private void RunConfiguredAction(GitFileBrowserActionConfig action, GitChangeInfo? selectedChange)
        {
            var result = _gitService.TryRunConfiguredAction(selectedChange, action);
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
