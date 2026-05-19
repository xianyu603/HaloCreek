using System;
using System.Collections.Generic;
using HaloCreek.Models;
using HaloCreek.Services;

namespace HaloCreek.ViewModels.Tabs
{
    public sealed class PromptEditorViewModel : ViewModelBase
    {
        private readonly ConfigService _configService;
        private readonly DragDropService _dragDropService;
        private readonly SessionLifecycleService _sessionLifecycleService;
        private string _promptText = string.Empty;
        private string? _workspacePath;

        public PromptEditorViewModel()
            : this(
                new DragDropService(),
                new SessionLifecycleService(),
                new ConfigService())
        {
        }

        public PromptEditorViewModel(
            DragDropService dragDropService,
            SessionLifecycleService sessionLifecycleService,
            ConfigService configService)
        {
            _dragDropService = dragDropService;
            _sessionLifecycleService = sessionLifecycleService;
            _configService = configService;
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

        public void SetWorkspacePath(string workspacePath)
        {
            WorkspacePath = workspacePath;
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

            var config = _configService.LoadEffectiveConfig(WorkspacePath);
            return _sessionLifecycleService.Launch(WorkspacePath, PromptText, config);
        }
    }
}
