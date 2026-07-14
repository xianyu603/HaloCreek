using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class GitViewModel : ViewModelBase, IDisposable
    {
        private readonly IWorkspaceSnapshotSource<GitSnapshot> _gitSnapshots;
        private readonly ExternalActionService _externalActionService;
        private readonly TransientEventService _transientEventService;
        private IReadOnlyList<GitChangeInfo> _changes = Array.Empty<GitChangeInfo>();
        private IReadOnlyList<GitSelectedPathActionDescriptor> _selectedFilePathActionDescriptors =
            Array.Empty<GitSelectedPathActionDescriptor>();
        private IReadOnlyList<IGitFileAction> _workspaceRootActions = Array.Empty<IGitFileAction>();
        private GitChangeInfo? _selectedChange;
        private GitSelectedPathActionDescriptor _doubleClickAction = null!;
        private bool _isDisposed;

        public GitViewModel(
            IWorkspaceSnapshotSource<GitSnapshot> gitSnapshots,
            ExternalActionService externalActionService,
            AppCommonRuntime appCommonRuntime,
            ICommand refreshCommand)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _gitSnapshots = gitSnapshots ?? throw new ArgumentNullException(nameof(gitSnapshots));
            _externalActionService = externalActionService
                ?? throw new ArgumentNullException(nameof(externalActionService));
            _transientEventService = appCommonRuntime.TransientEventService;
            RefreshCommand = refreshCommand ?? throw new ArgumentNullException(nameof(refreshCommand));
            OpenSelectedChangeCommand = new RelayCommand<GitChangeInfo>(OpenSelectedChange, CanOpenSelectedChange);
            LoadActionConfig();
            _gitSnapshots.Changed += OnGitSnapshotChanged;
            var snapshot = _gitSnapshots.Current;
            SelectedChange = null;
            Changes = snapshot.Changes;
            Log.Info("Git", snapshot.Message);
        }

        public IReadOnlyList<GitChangeInfo> Changes
        {
            get => _changes;
            private set => SetProperty(ref _changes, value);
        }

        public GitChangeInfo? SelectedChange
        {
            get => _selectedChange;
            set => SetProperty(ref _selectedChange, value);
        }

        public ICommand RefreshCommand { get; }

        public IReadOnlyList<IGitFileAction> WorkspaceRootActions
        {
            get => _workspaceRootActions;
            private set => SetProperty(ref _workspaceRootActions, value);
        }

        public IRelayCommand<GitChangeInfo> OpenSelectedChangeCommand { get; }

        public IReadOnlyList<IGitFileAction> GetFilePathActions(GitChangeInfo change)
        {
            ArgumentNullException.ThrowIfNull(change);
            return BuildSelectedFilePathActions(_selectedFilePathActionDescriptors, change);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _gitSnapshots.Changed -= OnGitSnapshotChanged;
        }

        private void OnGitSnapshotChanged(object? sender, EventArgs e)
        {
            if (_isDisposed)
            {
                return;
            }

            var snapshot = _gitSnapshots.Current;
            SelectedChange = null;
            Changes = snapshot.Changes;
            Log.Info("Git", snapshot.Message);
        }

        private void LoadActionConfig()
        {
            _doubleClickAction = _externalActionService.GetGitSelectedPathDoubleClickAction();
            _selectedFilePathActionDescriptors = _externalActionService
                .GetGitSelectedPathActions()
                .ToArray();
            var workspaceRootActions = BuildWorkspaceRootActions(
                _externalActionService.GetGitWorkspaceRootActions());
            WorkspaceRootActions = new IGitFileAction[]
                {
                    new GitFileAction("Refresh", RefreshCommand),
                }
                .Concat(workspaceRootActions)
                .ToArray();
        }

        private IReadOnlyList<IGitFileAction> BuildSelectedFilePathActions(
            IEnumerable<GitSelectedPathActionDescriptor> actions,
            GitChangeInfo change)
        {
            return actions
                .Select(action => new GitFileAction(
                    action.Title,
                    new RelayCommand(
                        () => RunGitSelectedPathAction(action, change),
                        () => !string.IsNullOrWhiteSpace(change.RelativePath))))
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
