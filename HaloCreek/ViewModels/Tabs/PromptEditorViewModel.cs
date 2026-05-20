using System;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class PromptEditorViewModel : ViewModelBase
    {
        private readonly ConfigService _configService;
        private readonly SessionLifecycleService _sessionLifecycleService;
        private string _promptText = string.Empty;
        private Action<string>? _statusDispatcher;
        private string? _workspacePath;

        public PromptEditorViewModel(
            SessionLifecycleService sessionLifecycleService,
            ConfigService configService)
        {
            _sessionLifecycleService = sessionLifecycleService;
            _configService = configService;

            LaunchCommand = new RelayCommand(Launch, CanLaunchPrompt);
        }

        public string PromptText
        {
            get => _promptText;
            set
            {
                if (SetProperty(ref _promptText, value ?? string.Empty))
                {
                    LaunchCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string? WorkspacePath
        {
            get => _workspacePath;
            private set => SetProperty(ref _workspacePath, value);
        }

        public IRelayCommand LaunchCommand { get; }

        public void SetWorkspacePath(string workspacePath)
        {
            WorkspacePath = workspacePath;
            LaunchCommand.NotifyCanExecuteChanged();
        }

        public void SetStatusDispatcher(Action<string> statusDispatcher)
        {
            _statusDispatcher = statusDispatcher ?? throw new ArgumentNullException(nameof(statusDispatcher));
        }

        public SessionLaunchResult LaunchPrompt()
        {
            if (string.IsNullOrWhiteSpace(WorkspacePath))
            {
                return new SessionLaunchResult(false, "No workspace selected.", null);
            }

            if (string.IsNullOrWhiteSpace(PromptText))
            {
                return new SessionLaunchResult(false, "Prompt is empty.", null);
            }

            var config = _configService.LoadEffectiveConfig(WorkspacePath);
            return _sessionLifecycleService.Launch(WorkspacePath, PromptText, config);
        }

        private bool CanLaunchPrompt()
        {
            return !string.IsNullOrWhiteSpace(WorkspacePath)
                && !string.IsNullOrWhiteSpace(PromptText);
        }

        private void Launch()
        {
            var result = LaunchPrompt();
            _statusDispatcher?.Invoke(result.StatusMessage);
        }
    }
}
