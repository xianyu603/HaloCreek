using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using HaloCreek.Infrastructure;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class SessionLifecycleService
    {
        private readonly PlatformInfrastructure _platformInfrastructure;

        public SessionLifecycleService(PlatformInfrastructure platformInfrastructure)
        {
            _platformInfrastructure = platformInfrastructure ?? throw new ArgumentNullException(nameof(platformInfrastructure));
        }

        public SessionLaunchResult Launch(
            string workspacePath,
            string promptText,
            AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new SessionLaunchResult(false, "No workspace selected.", null);
            }

            if (string.IsNullOrWhiteSpace(promptText))
            {
                return new SessionLaunchResult(false, "Prompt is empty.", null);
            }

            if (config is null)
            {
                return new SessionLaunchResult(false, "Config is not available.", null);
            }

            if (!Directory.Exists(workspacePath))
            {
                return new SessionLaunchResult(false, "Workspace path does not exist.", null);
            }

            try
            {
                var process = _platformInfrastructure.LaunchWslTerminalCommand(
                    workspacePath,
                    config.CodexExecutableName,
                    config.CodexLaunchArguments.Concat(new[] { promptText }));
                if (process is null)
                {
                    return new SessionLaunchResult(false, "Failed to start session terminal.", null);
                }

                var now = DateTimeOffset.Now;
                var session = new OngoingSessionInfo(
                    process.Id.ToString(),
                    "Codex session",
                    workspacePath,
                    now,
                    now,
                    OngoingSessionState.Starting);

                return new SessionLaunchResult(true, "Codex session launch requested.", session);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or Win32Exception
                or IOException
                or UnauthorizedAccessException)
            {
                return new SessionLaunchResult(false, $"Failed to start session: {ex.Message}", null);
            }
        }

        public IReadOnlyList<OngoingSessionInfo> GetOngoingSessions(string? workspacePath)
        {
            return Array.Empty<OngoingSessionInfo>();
        }

        public bool TryBringToFront(OngoingSessionInfo session)
        {
            ArgumentNullException.ThrowIfNull(session);

            return false;
        }
    }

    public sealed record SessionLaunchResult(
        bool Started,
        string StatusMessage,
        OngoingSessionInfo? Session);
}
