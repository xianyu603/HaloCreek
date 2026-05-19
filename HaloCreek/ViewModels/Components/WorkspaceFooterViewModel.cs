using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Infrastructure;

namespace HaloCreek.ViewModels.Components
{
    public sealed class WorkspaceFooterViewModel : ViewModelBase
    {
        private const string NoWorkspaceSelectedText = "No workspace selected";
        private const string ReadyStatusText = "Ready";
        private const string SelectingWorkspaceStatusText = "Selecting workspace...";
        private const string InvalidWorkspacePathStatusText = "Invalid workspace path";
        private const string WorkspaceDispatcherNotConnectedStatusText = "Workspace dispatcher is not connected";

        private readonly PlatformInfrastructure _platformInfrastructure;
        private Action<string>? _setWorkspace;
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

        public void SetWorkspaceDispatcher(Action<string> setWorkspace)
        {
            _setWorkspace = setWorkspace ?? throw new ArgumentNullException(nameof(setWorkspace));
        }

        private async Task ChooseWorkspaceAsync()
        {
            if (_setWorkspace is null)
            {
                StatusText = WorkspaceDispatcherNotConnectedStatusText;
                return;
            }

            StatusText = SelectingWorkspaceStatusText;

            var selectedPath = await _platformInfrastructure.SelectDirectoryAsync();
            if (selectedPath is null)
            {
                StatusText = ReadyStatusText;
                return;
            }

            string normalizedPath;
            try
            {
                normalizedPath = _platformInfrastructure.NormalizeDirectoryPath(selectedPath);
            }
            catch (Exception ex) when (ex is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                StatusText = InvalidWorkspacePathStatusText;
                return;
            }

            if (!_platformInfrastructure.IsValidDirectoryPath(normalizedPath))
            {
                StatusText = InvalidWorkspacePathStatusText;
                return;
            }

            _setWorkspace(normalizedPath);
            StatusText = ReadyStatusText;
        }
    }
}
