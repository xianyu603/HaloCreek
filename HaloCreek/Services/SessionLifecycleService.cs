using System;
using System.Collections.Generic;
using System.Linq;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class SessionLifecycleService
    {
        private readonly TmuxService _tmuxService;
        private readonly TerminalService _terminalService;

        public SessionLifecycleService(
            TmuxService tmuxService,
            TerminalService terminalService)
        {
            _tmuxService = tmuxService ?? throw new ArgumentNullException(nameof(tmuxService));
            _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
            _tmuxService.StateChanged += HandleTmuxStateChanged;
        }

        // 只向外部汇报session或其状态发生了变化 先不实现复杂的消息通知 卡了再优化
        public event EventHandler? SessionsChanged;

        // TODO 可以考虑这里的部分错误throw 之后再说
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

            var identifier = _tmuxService.Launch(new TmuxLaunchRequest(
                workspacePath,
                config.CodexExecutableName,
                config.CodexLaunchArguments.Concat(new[] { promptText }).ToArray(),
                "Codex session"));
            var now = DateTimeOffset.Now;
            var session = new OngoingSessionInfo(
                identifier,
                "Codex session",
                workspacePath,
                now,
                OngoingSessionState.BackgroundRunning);

            SessionsChanged?.Invoke(this, EventArgs.Empty);

            return new SessionLaunchResult(true, "Codex session launch requested.", session);
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

            if (string.IsNullOrWhiteSpace(currentWorkspacePath))
            {
                return new SessionResumeResult(false, "No workspace selected.");
            }

            if (string.IsNullOrWhiteSpace(session.Id))
            {
                return new SessionResumeResult(false, "Session id is empty.");
            }

            if (config is null)
            {
                return new SessionResumeResult(false, "Config is not available.");
            }

            _tmuxService.Launch(new TmuxLaunchRequest(
                currentWorkspacePath,
                config.CodexExecutableName,
                config.CodexLaunchArguments.Concat(new[] { "resume", session.Id }).ToArray(),
                "Codex resume session"));

            SessionsChanged?.Invoke(this, EventArgs.Empty);

            return new SessionResumeResult(true, "Codex session resume requested.");
        }

        public IReadOnlyList<OngoingSessionInfo> GetOngoingSessions(string? workspacePath)
        {
            return Array.Empty<OngoingSessionInfo>();
        }

        public void BringToFront(string sessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        }

        public void Exit(string sessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        }

        public void Cleanup()
        {
        }

        private void HandleTmuxStateChanged(object? sender, TmuxSessionStateChangedEventArgs args)
        {
            SessionsChanged?.Invoke(this, EventArgs.Empty);
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
