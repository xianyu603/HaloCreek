using System;
using Avalonia.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class WorkspaceRuntimeService
    {
        private const string InvalidStartupWorkspacePathMessage = "Startup workspace path is invalid or unavailable.";

        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly WorkspaceCacheService _workspaceCacheService;
        private readonly ConfigService _configService;
        private string? _currentWorkspacePath;
        private AppConfig? _effectiveConfig;

        public WorkspaceRuntimeService(
            AppCommonRuntime appCommonRuntime,
            WorkspaceCacheService workspaceCacheService,
            ConfigService configService)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _platformInfrastructure = appCommonRuntime.PlatformInfrastructure;
            _workspaceCacheService = workspaceCacheService
                ?? throw new ArgumentNullException(nameof(workspaceCacheService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public event EventHandler<WorkspaceRuntimeChangedEventArgs>? WorkspaceChangedEvent;

        public string? CurrentWorkspacePath => _currentWorkspacePath;

        public AppConfig EffectiveConfig => _effectiveConfig
            ?? throw new InvalidOperationException("Workspace runtime is not initialized.");

        public void InitializeStartupWorkspace()
        {
            InitializeStartupWorkspace(ResolveStartupWorkspacePath());
        }

        private void InitializeStartupWorkspace(string workspacePath)
        {
            if (!_platformInfrastructure.TryNormalizeExistingDirectoryPath(workspacePath, out var normalizedPath))
            {
                throw new InvalidOperationException($"{InvalidStartupWorkspacePathMessage} Path: {workspacePath}");
            }

            ApplyWorkspacePath(normalizedPath, cacheWorkspace: false);
        }

        public bool SetWorkspacePath(string workspacePath)
        {
            if (!_platformInfrastructure.TryNormalizeExistingDirectoryPath(workspacePath, out var normalizedPath))
            {
                return false;
            }

            ApplyWorkspacePath(normalizedPath, cacheWorkspace: true);

            return true;
        }

        private void ApplyWorkspacePath(string normalizedWorkspacePath, bool cacheWorkspace)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(normalizedWorkspacePath);

            var effectiveConfig = _configService.LoadEffectiveConfig(normalizedWorkspacePath);

            _currentWorkspacePath = normalizedWorkspacePath;
            _effectiveConfig = effectiveConfig;

            if (cacheWorkspace)
            {
                _workspaceCacheService.TrySaveLastWorkspacePath(normalizedWorkspacePath);
            }

            var workspaceChanged = WorkspaceChangedEvent;
            if (workspaceChanged is null)
            {
                return;
            }

            var changedEventArgs = new WorkspaceRuntimeChangedEventArgs(normalizedWorkspacePath, effectiveConfig);
            Dispatcher.UIThread.Post(() => workspaceChanged.Invoke(this, changedEventArgs));
        }

        private string ResolveStartupWorkspacePath()
        {
            var cachedWorkspacePath = _workspaceCacheService.LoadLastWorkspacePath();
            if (_platformInfrastructure.TryNormalizeExistingDirectoryPath(cachedWorkspacePath, out var normalizedPath))
            {
                return normalizedPath;
            }

            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (_platformInfrastructure.TryNormalizeExistingDirectoryPath(homePath, out normalizedPath))
            {
                return normalizedPath;
            }

            return AppContext.BaseDirectory;
        }
    }
}
