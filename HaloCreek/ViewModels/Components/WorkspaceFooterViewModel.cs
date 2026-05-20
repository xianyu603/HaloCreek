using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Infrastructure;

namespace HaloCreek.ViewModels.Components
{
    public sealed class WorkspaceFooterViewModel : ViewModelBase
    {
        private const string NoWorkspaceSelectedText = "No workspace selected";
        private const string ReadyStatusText = "Ready";
        private const string InvalidWorkspacePathStatusText = "Invalid workspace path";
        private const string CanceledWorkspaceSelectionStatusText = "Canceled selected workspace.";
        private const string SelectingWorkspaceStatusText = "Selecting workspace...";
        private const string WorkspaceDispatcherNotConnectedStatusText = "Workspace dispatcher is not connected";

        private readonly PlatformInfrastructure _platformInfrastructure;
        private Action<string>? _applyValidatedWorkspace;
        private string _workspacePath = NoWorkspaceSelectedText;
        private string _statusText = ReadyStatusText;

        public WorkspaceFooterViewModel(PlatformInfrastructure platformInfrastructure)
        {
            _platformInfrastructure = platformInfrastructure ?? throw new ArgumentNullException(nameof(platformInfrastructure));
            ChooseWorkspaceCommand = new AsyncRelayCommand(ChooseWorkspaceAsync);
        }

        public string WorkspacePath
        {
            get => _workspacePath;
            set => SetProperty(ref _workspacePath, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public IAsyncRelayCommand ChooseWorkspaceCommand { get; }

        public void SetWorkspacePath(string workspacePath)
        {
            WorkspacePath = workspacePath;
        }

        public void SetWorkspaceDispatcher(Action<string> applyValidatedWorkspace)
        {
            _applyValidatedWorkspace = applyValidatedWorkspace ?? throw new ArgumentNullException(nameof(applyValidatedWorkspace));
        }

        private async Task ChooseWorkspaceAsync()
        {
            if (_applyValidatedWorkspace is null)
            {
                StatusText = WorkspaceDispatcherNotConnectedStatusText;
                return;
            }

            StatusText = SelectingWorkspaceStatusText;

            string? selectedPath;
            try
            {
                selectedPath = await _platformInfrastructure.SelectDirectoryAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Workspace picker failed: {ex.Message}";
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                StatusText = CanceledWorkspaceSelectionStatusText;
                return;
            }

            if (!_platformInfrastructure.TryNormalizeExistingDirectoryPath(selectedPath, out var normalizedPath))
            {
                StatusText = InvalidWorkspacePathStatusText;
                return;
            }

            _applyValidatedWorkspace(normalizedPath);
            StatusText = $"Selected workspace {normalizedPath}.";
            //TODO Status Bar的优先级和具体的产品定位待进一步设计
        }
    }
}
