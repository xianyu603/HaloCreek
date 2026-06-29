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

        public WorkspaceStartupSelector(
            AppCommonRuntime appCommonRuntime,
            WorkspaceCacheService workspaceCacheService)
        {
            _appCommonRuntime = appCommonRuntime
                ?? throw new ArgumentNullException(nameof(appCommonRuntime));
            _workspaceCacheService = workspaceCacheService
                ?? throw new ArgumentNullException(nameof(workspaceCacheService));
        }

        public async Task<WorkspaceContext> SelectRequiredWorkspaceAsync(string[]? startupArgs)
        {
            var startupWorkspacePath = GetStartupWorkspacePath(startupArgs);
            if (!string.IsNullOrWhiteSpace(startupWorkspacePath))
            {
                return WorkspaceRuntime.SwitchWorkspace(startupWorkspacePath);
            }

            var cachedWorkspacePath = _workspaceCacheService.LoadLastWorkspacePath();
            if (!string.IsNullOrWhiteSpace(cachedWorkspacePath))
            {
                try
                {
                    return WorkspaceRuntime.SwitchWorkspace(cachedWorkspacePath);
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
                    return WorkspaceRuntime.SwitchWorkspace(selectedPath);
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

        private static string? GetStartupWorkspacePath(string[]? startupArgs)
        {
            if (startupArgs is null)
            {
                return null;
            }

            foreach (var arg in startupArgs)
            {
                if (!string.IsNullOrWhiteSpace(arg))
                {
                    return arg.Trim();
                }
            }

            return null;
        }
    }
}
