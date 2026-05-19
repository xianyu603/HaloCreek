using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class PromptEditorViewModel : ViewModelBase
    {
        private const string FixedLaunchPromptText = "Hello from HaloCreek MVP1 prompt editor.";

        private readonly ConfigService _configService;
        private readonly DragDropService _dragDropService;
        private readonly SessionLifecycleService _sessionLifecycleService;
        private string _promptText = string.Empty;
        private Action<string>? _statusDispatcher;
        private string? _workspacePath;

        public PromptEditorViewModel(
            DragDropService dragDropService,
            SessionLifecycleService sessionLifecycleService,
            ConfigService configService)
        {
            _dragDropService = dragDropService;
            _sessionLifecycleService = sessionLifecycleService;
            _configService = configService;

            LaunchCommand = new RelayCommand(Launch, CanLaunchPrompt);
        }

        public string PromptText
        {
            get => _promptText;
            set => SetProperty(ref _promptText, value);
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

        public string? GetPrimaryDroppedPath(IEnumerable<string>? paths)
        {
            return _dragDropService.GetPrimaryDroppedPath(paths);
        }

        public SessionLaunchResult LaunchPrompt()
        {
            if (string.IsNullOrWhiteSpace(WorkspacePath))
            {
                return new SessionLaunchResult(false, "No workspace selected.", null);
            }

            if (string.IsNullOrWhiteSpace(FixedLaunchPromptText))
            {
                return new SessionLaunchResult(false, "Prompt is empty.", null);
            }

            var config = _configService.LoadEffectiveConfig(WorkspacePath);
            return _sessionLifecycleService.Launch(WorkspacePath, FixedLaunchPromptText, config);
        }

        private bool CanLaunchPrompt()
        {
            return !string.IsNullOrWhiteSpace(WorkspacePath)
                && !string.IsNullOrWhiteSpace(FixedLaunchPromptText);
        }

        private void Launch()
        {
            var result = LaunchPrompt();
            _statusDispatcher?.Invoke(result.StatusMessage);
        }
    }
}
