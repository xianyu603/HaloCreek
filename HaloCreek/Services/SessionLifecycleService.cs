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

        // TODO 可以考虑这里的部分错误throw 之后再说
        public SessionLaunchResult Launch(
            string workspacePath,
            string promptText,
            AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(promptText))
            {
                return new SessionLaunchResult(false, "Prompt is empty.", null);
            }

            var startResult = StartCodexTerminalCommand(
                workspacePath,
                config,
                new[] { promptText },
                "Failed to start session terminal.",
                "Failed to start session",
                "Codex session launch requested.");
            if (!startResult.Started)
            {
                return new SessionLaunchResult(false, startResult.StatusMessage, null);
            }

            var now = DateTimeOffset.Now;
            var session = new OngoingSessionInfo(
                startResult.ProcessId.ToString(),
                "Codex session",
                startResult.NormalizedWorkspacePath,
                now,
                now,
                OngoingSessionState.Starting);

            return new SessionLaunchResult(true, startResult.StatusMessage, session);
        }

        public SessionResumeResult Resume(
            HistorySessionInfo? session,
            string currentWorkspacePath,
            AppConfig config)
        {
            if (session is null)
            {
                return new SessionResumeResult(false, "No session selected.");
            }

            if (string.IsNullOrWhiteSpace(session.Id))
            {
                return new SessionResumeResult(false, "Session id is empty.");
            }

            var startResult = StartCodexTerminalCommand(
                currentWorkspacePath,
                config,
                new[] { "resume", session.Id },
                "Failed to start resume terminal.",
                "Failed to resume session",
                "Codex session resume requested.");
            return new SessionResumeResult(startResult.Started, startResult.StatusMessage);
        }

        private CodexTerminalCommandResult StartCodexTerminalCommand(
            string workspacePath,
            AppConfig? config,
            IEnumerable<string> commandArguments,
            string terminalFailureMessage,
            string exceptionStatusPrefix,
            string successStatusMessage)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return CodexTerminalCommandResult.Failed("No workspace selected.");
            }

            if (config is null)
            {
                return CodexTerminalCommandResult.Failed("Config is not available.");
            }

            if (!_platformInfrastructure.TryNormalizeExistingDirectoryPath(workspacePath, out var normalizedWorkspacePath))
            {
                return CodexTerminalCommandResult.Failed("Workspace path does not exist.");
            }

            try
            {
                var process = _platformInfrastructure.LaunchWslTerminalCommand(
                    normalizedWorkspacePath,
                    config.CodexExecutableName,
                    config.CodexLaunchArguments.Concat(commandArguments));
                if (process is null)
                {
                    return CodexTerminalCommandResult.Failed(terminalFailureMessage);
                }

                return new CodexTerminalCommandResult(
                    true,
                    successStatusMessage,
                    process.Id,
                    normalizedWorkspacePath);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or Win32Exception
                or IOException
                or UnauthorizedAccessException)
            {
                return CodexTerminalCommandResult.Failed($"{exceptionStatusPrefix}: {ex.Message}");
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

        private sealed record CodexTerminalCommandResult(
            bool Started,
            string StatusMessage,
            int ProcessId,
            string NormalizedWorkspacePath)
        {
            public static CodexTerminalCommandResult Failed(string statusMessage)
            {
                return new CodexTerminalCommandResult(false, statusMessage, 0, string.Empty);
            }
        }
    }

    public sealed record SessionLaunchResult(
        bool Started,
        string StatusMessage,
        OngoingSessionInfo? Session);

    public sealed record SessionResumeResult(
        bool Started,
        string StatusMessage);
}
