using System;
using System.IO;
using System.Threading.Tasks;
using HaloCreek.Logging;

namespace HaloCreek.Services
{
    public sealed class WorkspaceStartupSelector
    {
        private const string InvalidWorkspaceTitle = "Invalid workspace";
        private const string WorkspaceRequiredTitle = "Workspace required";
        private const string WorkspacePickerFailureTitle = "Workspace picker failed";

        private readonly AppCommonRuntime _appCommonRuntime;
        private readonly WorkspaceCacheService _workspaceCacheService;
        private readonly WorkspaceRuntimeLegacyBridge _workspaceRuntimeLegacyBridge;

        public WorkspaceStartupSelector(
            AppCommonRuntime appCommonRuntime,
            WorkspaceCacheService workspaceCacheService,
            WorkspaceRuntimeLegacyBridge workspaceRuntimeLegacyBridge)
        {
            _appCommonRuntime = appCommonRuntime
                ?? throw new ArgumentNullException(nameof(appCommonRuntime));
            _workspaceCacheService = workspaceCacheService
                ?? throw new ArgumentNullException(nameof(workspaceCacheService));
            _workspaceRuntimeLegacyBridge = workspaceRuntimeLegacyBridge
                ?? throw new ArgumentNullException(nameof(workspaceRuntimeLegacyBridge));
        }

        public async Task<WorkspaceContext> SelectRequiredWorkspaceAsync()
        {
            var cachedWorkspacePath = _workspaceCacheService.LoadLastWorkspacePath();
            if (!string.IsNullOrWhiteSpace(cachedWorkspacePath))
            {
                try
                {
                    return _workspaceRuntimeLegacyBridge.SwitchWorkspace(cachedWorkspacePath);
                }
                catch (Exception ex) when (ex is InvalidOperationException
                    or IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or ArgumentException)
                {
                    await _appCommonRuntime.PlatformInfrastructure.ShowErrorDialogAsync(
                        InvalidWorkspaceTitle,
                        "Last workspace can no longer be used. Select a valid Git repository root to continue."
                        + Environment.NewLine
                        + Environment.NewLine
                        + ex.Message);
                }
            }

            while (true)
            {
                string? selectedPath;
                try
                {
                    selectedPath = await _appCommonRuntime.PlatformInfrastructure.SelectDirectoryAsync();
                }
                catch (Exception ex)
                {
                    Log.Error("Workspace", ex, "Startup workspace picker failed.");
                    await _appCommonRuntime.PlatformInfrastructure.ShowErrorDialogAsync(
                        WorkspacePickerFailureTitle,
                        ex.Message);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    await _appCommonRuntime.PlatformInfrastructure.ShowMessageDialogAsync(
                        WorkspaceRequiredTitle,
                        "Select a Git repository root to continue.");
                    continue;
                }

                try
                {
                    return _workspaceRuntimeLegacyBridge.SwitchWorkspace(selectedPath);
                }
                catch (Exception ex) when (ex is InvalidOperationException
                    or IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or ArgumentException)
                {
                    await _appCommonRuntime.PlatformInfrastructure.ShowErrorDialogAsync(
                        InvalidWorkspaceTitle,
                        ex.Message);
                }
            }
        }
    }
}
