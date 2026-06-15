using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly ExternalActionService _externalActionService;
        private readonly TransientEventService _transientEventService;
        private IReadOnlyList<GitChangeInfo> _changes = Array.Empty<GitChangeInfo>();
        private IReadOnlyList<RelayCommand> _configuredActionCommands = Array.Empty<RelayCommand>();
        private IReadOnlyList<IGitFileAction> _selectedFilePathActions = Array.Empty<IGitFileAction>();
        private IReadOnlyList<IGitFileAction> _workspaceRootActions = Array.Empty<IGitFileAction>();
        private GitChangeInfo? _selectedChange;
        private GitSelectedPathActionDescriptor _doubleClickAction = null!;

        public GitViewModel(
            GitService gitService,
            ExternalActionService externalActionService,
            AppCommonRuntime appCommonRuntime,
            ICommand refreshCommand)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            _externalActionService = externalActionService
                ?? throw new ArgumentNullException(nameof(externalActionService));
            _transientEventService = appCommonRuntime.TransientEventService;
            RefreshCommand = refreshCommand ?? throw new ArgumentNullException(nameof(refreshCommand));
            OpenSelectedChangeCommand = new RelayCommand<GitChangeInfo>(OpenSelectedChange, CanOpenSelectedChange);
            LoadActionConfig();
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

        public async Task RefreshChangesAsync()
        {
            var result = await Task.Run(_gitService.GetChanges);

            if (!Changes.SequenceEqual(result.Changes))
            {
                SelectedChange = null;
                Changes = result.Changes;
            }

            Log.Info("Git", result.Message);
        }

        private void LoadActionConfig()
        {
            _doubleClickAction = _externalActionService.GetGitSelectedPathDoubleClickAction();
            var selectedFilePathActions = BuildSelectedFilePathActions(
                _externalActionService.GetGitSelectedPathActions());
            var workspaceRootActions = BuildWorkspaceRootActions(
                _externalActionService.GetGitWorkspaceRootActions());
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
            IEnumerable<GitSelectedPathActionDescriptor> actions)
        {
            return actions
                .Select(action => new GitFileAction(
                    action.Title,
                    new RelayCommand(
                        () => RunGitSelectedPathAction(action, SelectedChange),
                        () => !string.IsNullOrWhiteSpace(SelectedChange?.RelativePath))))
                .ToArray();
        }

        private IReadOnlyList<IGitFileAction> BuildWorkspaceRootActions(
            IEnumerable<GitWorkspaceRootActionDescriptor> actions)
        {
            return actions
                .Select(action => new GitFileAction(
                    action.Title,
                    new RelayCommand(
                        () => RunGitWorkspaceRootAction(action))))
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
            return change is not null;
        }

        private void OpenSelectedChange(GitChangeInfo? change)
        {
            if (change is null)
            {
                return;
            }

            RunGitSelectedPathAction(_doubleClickAction, change);
        }

        private void RunGitSelectedPathAction(
            GitSelectedPathActionDescriptor action,
            GitChangeInfo? selectedChange)
        {
            try
            {
                if (selectedChange is null)
                {
                    throw new InvalidOperationException("SelectedFilePath actions require a selected Git change.");
                }

                _externalActionService.RunGitSelectedPathAction(action.Id, selectedChange.RelativePath);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or ArgumentException
                or NotSupportedException)
            {
                _transientEventService.ReportUserActionFailure(
                    "Git",
                    "Git action failed",
                    ex.Message);
            }
        }

        private void RunGitWorkspaceRootAction(GitWorkspaceRootActionDescriptor action)
        {
            try
            {
                _externalActionService.RunGitWorkspaceRootAction(action.Id);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or ArgumentException
                or NotSupportedException)
            {
                _transientEventService.ReportUserActionFailure(
                    "Git",
                    "Git action failed",
                    ex.Message);
            }
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
