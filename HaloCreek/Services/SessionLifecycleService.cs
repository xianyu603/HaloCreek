using System;
using System.Collections.Generic;
using System.Linq;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class SessionLifecycleService
    {
        private readonly object _sessionsLock = new();
        private readonly Dictionary<string, OngoingSessionInfo> _sessionsById = new(StringComparer.Ordinal);
        private readonly TmuxService _tmuxService;
        private readonly TerminalService _terminalService;
        private string? _frontSessionId;

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

            lock (_sessionsLock)
            {
                _sessionsById.Add(identifier, session);
            }

            _tmuxService.StartWatching(identifier);
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

            var identifier = _tmuxService.Launch(new TmuxLaunchRequest(
                currentWorkspacePath,
                config.CodexExecutableName,
                config.CodexLaunchArguments.Concat(new[] { "resume", session.Id }).ToArray(),
                "Codex resume session"));
            var now = DateTimeOffset.Now;
            var ongoingSession = new OngoingSessionInfo(
                identifier,
                "Codex resume session",
                currentWorkspacePath,
                now,
                OngoingSessionState.BackgroundRunning);

            lock (_sessionsLock)
            {
                _sessionsById.Add(identifier, ongoingSession);
            }

            _tmuxService.StartWatching(identifier);
            SessionsChanged?.Invoke(this, EventArgs.Empty);

            return new SessionResumeResult(true, "Codex session resume requested.");
        }

        public IReadOnlyList<OngoingSessionInfo> GetOngoingSessions(string? workspacePath)
        {
            lock (_sessionsLock)
            {
                var sessions = _sessionsById.Values.AsEnumerable();
                if (!string.IsNullOrWhiteSpace(workspacePath))
                {
                    sessions = sessions.Where(session =>
                        string.Equals(session.WorkspacePath, workspacePath, StringComparison.Ordinal));
                }

                return sessions
                    .OrderBy(session => session.StartedAt)
                    .ToArray();
            }
        }

        public void BringToFront(string sessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            string? previousFrontSessionId = null;
            lock (_sessionsLock)
            {
                if (!_sessionsById.ContainsKey(sessionId))
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_frontSessionId)
                    && !string.Equals(_frontSessionId, sessionId, StringComparison.Ordinal)
                    && _sessionsById.TryGetValue(_frontSessionId, out var previousFrontSession))
                {
                    previousFrontSessionId = _frontSessionId;
                    _sessionsById[previousFrontSessionId] = previousFrontSession with
                    {
                        State = OngoingSessionState.Unknown
                    };
                    _frontSessionId = null;
                }
            }

            if (previousFrontSessionId is not null)
            {
                _tmuxService.StartWatching(previousFrontSessionId);
            }

            _tmuxService.StopWatching(sessionId);
            var command = _tmuxService.GetFrontCommand(sessionId);
            _terminalService.ShowFront(command);

            lock (_sessionsLock)
            {
                if (!_sessionsById.TryGetValue(sessionId, out var targetSession))
                {
                    return;
                }

                _sessionsById[sessionId] = targetSession with
                {
                    State = OngoingSessionState.Front
                };
                _frontSessionId = sessionId;
            }

            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Exit(string sessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            lock (_sessionsLock)
            {
                if (!_sessionsById.ContainsKey(sessionId))
                {
                    return;
                }
            }

            _tmuxService.StopWatching(sessionId);
            _tmuxService.Exit(sessionId);

            lock (_sessionsLock)
            {
                _sessionsById.Remove(sessionId);
                if (string.Equals(_frontSessionId, sessionId, StringComparison.Ordinal))
                {
                    _frontSessionId = null;
                }
            }

            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Cleanup()
        {
            string[] sessionIds;
            lock (_sessionsLock)
            {
                sessionIds = _sessionsById.Keys.ToArray();
            }

            foreach (var sessionId in sessionIds)
            {
                _tmuxService.StopWatching(sessionId);
                _tmuxService.Exit(sessionId);
            }

            lock (_sessionsLock)
            {
                _sessionsById.Clear();
                _frontSessionId = null;
            }

            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void HandleTmuxStateChanged(object? sender, TmuxSessionStateChangedEventArgs args)
        {
            if (args.State == OngoingSessionState.Front)
            {
                return;
            }

            var changed = false;
            lock (_sessionsLock)
            {
                if (!_sessionsById.TryGetValue(args.Identifier, out var session)
                    || string.Equals(_frontSessionId, args.Identifier, StringComparison.Ordinal)
                    || session.State == OngoingSessionState.Front)
                {
                    return;
                }

                if (session.State != args.State)
                {
                    _sessionsById[args.Identifier] = session with
                    {
                        State = args.State
                    };
                    changed = true;
                }
            }

            if (changed)
            {
                SessionsChanged?.Invoke(this, EventArgs.Empty);
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
