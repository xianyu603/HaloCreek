using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class GitViewModel : ViewModelBase
    {
        private readonly GitService _gitService;
        private readonly TransientEventService _transientEventService;
        private IReadOnlyList<GitChangeInfo> _changes = Array.Empty<GitChangeInfo>();
        private IReadOnlyList<RelayCommand> _configuredActionCommands = Array.Empty<RelayCommand>();
        private IReadOnlyList<IGitFileAction> _selectedFilePathActions = Array.Empty<IGitFileAction>();
        private IReadOnlyList<IGitFileAction> _workspaceRootActions = Array.Empty<IGitFileAction>();
        private GitChangeInfo? _selectedChange;
        private GitFileBrowserActionConfig? _doubleClickAction;
        private string _doubleClickActionId = string.Empty;
        private string? _workspacePath;

        public GitViewModel(
            GitService gitService,
            WorkspaceRuntimeService workspaceRuntimeService,
            AppCommonRuntime appCommonRuntime,
            ICommand refreshCommand)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            ArgumentNullException.ThrowIfNull(workspaceRuntimeService);
            _transientEventService = appCommonRuntime.TransientEventService;
            RefreshCommand = refreshCommand ?? throw new ArgumentNullException(nameof(refreshCommand));
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
                    NotifyConfiguredActionCanExecuteChanged();
                }
            }
        }

        public IReadOnlyList<GitChangeInfo> Changes
        {
            get => _changes;
            private set => SetProperty(ref _changes, value);
        }

        public GitChangeInfo? SelectedChange
        {
            get => _selectedChange;
            set
            {
                if (SetProperty(ref _selectedChange, value))
                {
                    NotifyConfiguredActionCanExecuteChanged();
                    OpenSelectedChangeCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public bool HasWorkspace => !string.IsNullOrWhiteSpace(WorkspacePath);

        public ICommand RefreshCommand { get; }

        public IReadOnlyList<IGitFileAction> SelectedFilePathActions
        {
            get => _selectedFilePathActions;
            private set => SetProperty(ref _selectedFilePathActions, value);
        }

        public IReadOnlyList<IGitFileAction> WorkspaceRootActions
        {
            get => _workspaceRootActions;
            private set => SetProperty(ref _workspaceRootActions, value);
        }

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
            var selectedFilePathActions = BuildSelectedFilePathActions(config.GitFileBrowserActions);
            var workspaceRootActions = BuildWorkspaceRootActions(config.GitFileBrowserActions);
            _configuredActionCommands = selectedFilePathActions
                .Concat(workspaceRootActions)
                .Select(action => action.Command)
                .OfType<RelayCommand>()
                .ToArray();
            SelectedFilePathActions = selectedFilePathActions;
            WorkspaceRootActions = new IGitFileAction[]
                {
                    new GitFileAction("Refresh", RefreshCommand),
                }
                .Concat(workspaceRootActions)
                .ToArray();
            NotifyConfiguredActionCanExecuteChanged();
            OpenSelectedChangeCommand.NotifyCanExecuteChanged();
        }

        private IReadOnlyList<IGitFileAction> BuildSelectedFilePathActions(
            IEnumerable<GitFileBrowserActionConfig> actions)
        {
            return actions
                .Where(action => action.Target == GitFileBrowserActionTarget.SelectedFilePath)
                .Select(action => new GitFileAction(
                    action.Label,
                    new RelayCommand(
                        () => RunConfiguredAction(action, SelectedChange),
                        () => HasWorkspace && SelectedChange is not null)))
                .ToArray();
        }

        private IReadOnlyList<IGitFileAction> BuildWorkspaceRootActions(
            IEnumerable<GitFileBrowserActionConfig> actions)
        {
            return actions
                .Where(action => action.Target == GitFileBrowserActionTarget.WorkspaceRoot)
                .Select(action => new GitFileAction(
                    action.Label,
                    new RelayCommand(
                        () => RunConfiguredAction(action, null),
                        () => HasWorkspace)))
                .ToArray();
        }

        private void NotifyConfiguredActionCanExecuteChanged()
        {
            foreach (var command in _configuredActionCommands)
            {
                command.NotifyCanExecuteChanged();
            }
        }

        private bool CanOpenSelectedChange(GitChangeInfo? change)
        {
            return HasWorkspace && change is not null;
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

        private sealed class GitFileAction : IGitFileAction
        {
            public GitFileAction(string label, ICommand command)
            {
                Label = label ?? throw new ArgumentNullException(nameof(label));
                Command = command ?? throw new ArgumentNullException(nameof(command));
            }

            public string Label { get; }

            public ICommand Command { get; }
        }
    }
}
