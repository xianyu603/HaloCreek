using System;
using System.Linq;
using HaloCreek.Infrastructure;

namespace HaloCreek.Services
{
    public static class WorkspaceRuntime
    {
        private const string GitExecutableName = "git";

        private static PlatformInfrastructure? _platformInfrastructure;
        private static WorkspaceCacheService? _workspaceCacheService;
        private static ConfigService? _configService;
        private static WorkspaceContext? _current;

        public static WorkspaceContext Current =>
            _current ?? throw new InvalidOperationException("Workspace is not valid.");

        public static void Initialize(
            AppCommonRuntime appCommonRuntime,
            WorkspaceCacheService workspaceCacheService,
            ConfigService configService)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);
            ArgumentNullException.ThrowIfNull(workspaceCacheService);
            ArgumentNullException.ThrowIfNull(configService);

            _platformInfrastructure = appCommonRuntime.PlatformInfrastructure;
            _workspaceCacheService = workspaceCacheService;
            _configService = configService;
        }

        public static WorkspaceContext SwitchWorkspace(string requestedPath)
        {
            var workspaceCacheService = _workspaceCacheService
                ?? throw new InvalidOperationException("Workspace runtime has not been initialized.");

            var context = GetWorkSpaceContextOfPath(requestedPath);

            _current = context;

            workspaceCacheService.SaveLastWorkspacePath(context.WorkspacePath);

            return context;
        }

        // 同时进行校验 解析 路径归一化 TODO 看得很难受之后再想想办法
        public static WorkspaceContext GetWorkSpaceContextOfPath(string requestedPath)
        {
            var platformInfrastructure = _platformInfrastructure
                ?? throw new InvalidOperationException("Workspace runtime has not been initialized.");
            var configService = _configService
                ?? throw new InvalidOperationException("Workspace runtime has not been initialized.");

            if (!platformInfrastructure.TryNormalizeExistingDirectoryPath(requestedPath, out var normalizedPath))
            {
                throw new InvalidOperationException(
                    "Selected workspace path is empty, unavailable, or not an existing directory.");
            }

            var effectiveConfig = configService.LoadEffectiveConfig(normalizedPath);

            var gitRootPath = ResolveGitRoot(normalizedPath);
            if (!PlatformInfrastructure.AreWorkspacePathsEquivalent(normalizedPath, gitRootPath))
            {
                throw new InvalidOperationException(
                    "Selected workspace is inside a Git repository. Select the Git repository root instead."
                    + Environment.NewLine
                    + $"Git root: {gitRootPath}");
            }

            return new WorkspaceContext(normalizedPath, effectiveConfig, normalizedPath);
        }

        private static string ResolveGitRoot(string normalizedPath)
        {
            var commandResult = PlatformInfrastructure.RunProcessWithCapturedOutput(
                GitExecutableName,
                new[] { "rev-parse", "--show-toplevel" },
                normalizedPath);

            if (!commandResult.Succeeded)
            {
                var message = commandResult.ErrorMessage;
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = "Selected workspace is not a Git repository root.";
                }

                throw new InvalidOperationException(message.Trim());
            }

            var gitRootCandidate = commandResult.Output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(gitRootCandidate))
            {
                throw new InvalidOperationException(
                    "Git root could not be resolved for the selected workspace.");
            }

            var platformInfrastructure = _platformInfrastructure
                ?? throw new InvalidOperationException("Workspace runtime has not been initialized.");
            if (!platformInfrastructure.TryNormalizeExistingDirectoryPath(gitRootCandidate, out var gitRootPath))
            {
                throw new InvalidOperationException(
                    $"Resolved Git root is unavailable. Path: {gitRootCandidate}");
            }

            return gitRootPath;
        }
    }
}
