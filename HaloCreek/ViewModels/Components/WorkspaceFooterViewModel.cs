using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Infrastructure;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Components
{
    public sealed class WorkspaceFooterViewModel : ViewModelBase
    {
        private const string NoWorkspaceSelectedText = "No workspace selected";
        private const string InvalidWorkspacePathStatusText = "Invalid workspace path";
        private const string SelectingWorkspaceStatusText = "Selecting workspace...";
        private const string WorkspaceCategory = "Workspace";

        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly ApplicationStatusService _applicationStatusService;
        private readonly TransientEventService _transientEventService;
        private readonly Func<string, bool> _setWorkspacePath;
        private string _workspacePath = NoWorkspaceSelectedText;
        private string _statusText = string.Empty;

        public WorkspaceFooterViewModel(
            WorkspaceRuntimeService workspaceRuntimeService,
            AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _platformInfrastructure = appCommonRuntime.PlatformInfrastructure;
            ArgumentNullException.ThrowIfNull(workspaceRuntimeService);
            _setWorkspacePath = workspaceRuntimeService.SetWorkspacePath;
            _applicationStatusService = appCommonRuntime.ApplicationStatusService;
            _transientEventService = appCommonRuntime.TransientEventService;
            _applicationStatusService.StatusTextChanged += OnStatusTextChanged;
            ChooseWorkspaceCommand = new AsyncRelayCommand(ChooseWorkspaceAsync);
            workspaceRuntimeService.ApplyCurrentWorkspaceAndSubscribe(OnWorkspaceChanged);
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

            if (!_setWorkspacePath(selectedPath))
            {
                _transientEventService.ReportUserActionFailure(
                    WorkspaceCategory,
                    InvalidWorkspacePathStatusText,
                    "Selected workspace path is invalid or unavailable.");
                return;
            }
        }

        private void OnStatusTextChanged(string statusText)
        {
            StatusText = statusText;
        }

        private void OnWorkspaceChanged(object? sender, WorkspaceRuntimeChangedEventArgs e)
        {
            WorkspacePath = e.WorkspacePath;
        }
    }
}
