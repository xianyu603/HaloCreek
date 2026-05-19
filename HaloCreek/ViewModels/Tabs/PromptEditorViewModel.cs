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
        private WorkspaceInfo? _workspace;

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

        public WorkspaceInfo? Workspace
        {
            get => _workspace;
            private set => SetProperty(ref _workspace, value);
        }

        public void SetWorkspace(WorkspaceInfo workspace)
        {
            Workspace = workspace;
        }

        public string? GetPrimaryDroppedPath(IEnumerable<string>? paths)
        {
            return _dragDropService.GetPrimaryDroppedPath(paths);
        }

        public SessionLaunchResult LaunchPrompt()
        {
            if (Workspace is null)
            {
                return new SessionLaunchResult(false, "No workspace selected.", null);
            }

            var config = _configService.LoadEffectiveConfig(Workspace);
            return _sessionLifecycleService.Launch(Workspace, PromptText, config);
        }
    }
}
