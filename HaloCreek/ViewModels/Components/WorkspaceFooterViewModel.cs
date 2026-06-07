using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Infrastructure;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Components
{
    public sealed class WorkspaceFooterViewModel : ViewModelBase, IDisposable
    {
        private const string NoWorkspaceSelectedText = "No workspace selected";
        private const string SelectingWorkspaceStatusText = "Selecting workspace...";
        private const string WorkspaceCategory = "Workspace";
        private const string WorkspaceSwitchFailureTitle = "Workspace switch failed";

        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly ApplicationStatusService _applicationStatusService;
        private readonly TransientEventService _transientEventService;
        private string _workspacePath = NoWorkspaceSelectedText;
        private string _statusText = string.Empty;
        private bool _isDisposed;

        public WorkspaceFooterViewModel(AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _platformInfrastructure = appCommonRuntime.PlatformInfrastructure;
            _applicationStatusService = appCommonRuntime.ApplicationStatusService;
            _transientEventService = appCommonRuntime.TransientEventService;
            _applicationStatusService.StatusTextChanged += OnStatusTextChanged;
            WorkspaceRuntime.Changed += OnWorkspaceChanged;
            ChooseWorkspaceCommand = new AsyncRelayCommand(ChooseWorkspaceAsync);
            OnWorkspaceChanged(WorkspaceRuntime.Current);
        }

        public string WorkspacePath
        {
            get => _workspacePath;
            set => SetProperty(ref _workspacePath, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public IAsyncRelayCommand ChooseWorkspaceCommand { get; }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _applicationStatusService.StatusTextChanged -= OnStatusTextChanged;
            WorkspaceRuntime.Changed -= OnWorkspaceChanged;
        }

        private async Task ChooseWorkspaceAsync()
        {
            var selectionStatus = _applicationStatusService.BeginBackgroundTask(SelectingWorkspaceStatusText);

            string? selectedPath;
            try
            {
                selectedPath = await _platformInfrastructure.SelectDirectoryAsync();
            }
            catch (Exception ex)
            {
                _applicationStatusService.Clear(selectionStatus);
                _transientEventService.ReportUserActionFailure(
                    WorkspaceCategory,
                    "Workspace picker failed",
                    ex.Message,
                    ex);
                return;
            }

            _applicationStatusService.Clear(selectionStatus);

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            try
            {
                WorkspaceRuntime.SwitchWorkspace(selectedPath);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or System.IO.IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException)
            {
                _transientEventService.ReportUserActionFailure(
                    WorkspaceCategory,
                    WorkspaceSwitchFailureTitle,
                    ex.Message,
                    ex);
                return;
            }
        }

        private void OnStatusTextChanged(string statusText)
        {
            StatusText = statusText;
        }

        private void OnWorkspaceChanged(WorkspaceContext workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            WorkspacePath = workspace.WorkspacePath;
        }
    }
}
