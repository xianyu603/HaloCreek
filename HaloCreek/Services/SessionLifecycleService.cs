using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class SessionLifecycleService : IDisposable
    {
        // 正确性前提是所有共享状态的直接操作都来自同一个 UI 线程。
        private readonly Dictionary<string, OngoingSessionInfo> _sessionsById = new(StringComparer.Ordinal);
        private readonly TmuxService _tmuxService;
        private readonly TerminalService _terminalService;
        private string? _frontSessionId;
        private bool _isDisposed;

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

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _tmuxService.StateChanged -= HandleTmuxStateChanged;

            var sessionIds = _sessionsById.Keys.ToArray();

            foreach (var sessionId in sessionIds)
            {
                try
                {
                    _tmuxService.StopWatching(sessionId);
                }
                catch (Exception)
                {
                    // Application shutdown must continue even if tmux cleanup fails.
                }

                try
                {
                    _tmuxService.Exit(sessionId);
                }
                catch (Exception)
                {
                    // Application shutdown must continue even if tmux cleanup fails.
                }
            }

            _sessionsById.Clear();
            _frontSessionId = null;
        }

        // TODO 可以考虑这里的部分错误throw 之后再说
        public SessionLaunchResult Launch(
            string workspacePath,
            string promptText,
            AppConfig config)
        {
            RequireUiThread();

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

            string identifier;
            try
            {
                identifier = _tmuxService.Launch(new TmuxLaunchRequest(
                    workspacePath,
                    config.CodexExecutableName,
                    config.CodexLaunchArguments.Concat(new[] { promptText }).ToArray(),
                    "Codex session"));
            }
            catch (InvalidOperationException ex)
            {
                return new SessionLaunchResult(false, ex.Message, null);
            }

            var now = DateTimeOffset.Now;
            var session = new OngoingSessionInfo(
                identifier,
                "Codex session",
                workspacePath,
                now,
                OngoingSessionState.BackgroundRunning);

            _sessionsById.Add(identifier, session);

            _tmuxService.StartWatching(identifier);
            SessionsChanged?.Invoke(this, EventArgs.Empty);

            return new SessionLaunchResult(true, "Codex session launch requested.", session);
        }

        public SessionResumeResult Resume(
            HistorySessionInfo? session,
            string currentWorkspacePath,
            AppConfig config)
        {
            RequireUiThread();

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

            string identifier;
            try
            {
                identifier = _tmuxService.Launch(new TmuxLaunchRequest(
                    currentWorkspacePath,
                    config.CodexExecutableName,
                    config.CodexLaunchArguments.Concat(new[] { "resume", session.Id }).ToArray(),
                    "Codex resume session"));
            }
            catch (InvalidOperationException ex)
            {
                return new SessionResumeResult(false, ex.Message);
            }

            var now = DateTimeOffset.Now;
            var ongoingSession = new OngoingSessionInfo(
                identifier,
                "Codex resume session",
                currentWorkspacePath,
                now,
                OngoingSessionState.BackgroundRunning);

            _sessionsById.Add(identifier, ongoingSession);

            _tmuxService.StartWatching(identifier);
            SessionsChanged?.Invoke(this, EventArgs.Empty);

            return new SessionResumeResult(true, "Codex session resume requested.");
        }

        public IReadOnlyList<OngoingSessionInfo> GetOngoingSessions(string? workspacePath)
        {
            RequireUiThread();

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

        public void BringToFront(string sessionId)
        {
            RequireUiThread();
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            string? previousFrontSessionId = null;
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

            if (previousFrontSessionId is not null)
            {
                _tmuxService.StartWatching(previousFrontSessionId);
            }

            _tmuxService.StopWatching(sessionId);
            var startupCommand = _tmuxService.GetFrontClientStartupCommand(sessionId);
            _terminalService.EnsureFrontClient(startupCommand);
            _tmuxService.SwitchFrontClient(sessionId);

            if (!_sessionsById.TryGetValue(sessionId, out var targetSession))
            {
                return;
            }

            _sessionsById[sessionId] = targetSession with
            {
                State = OngoingSessionState.Front
            };
            _frontSessionId = sessionId;

            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Exit(string sessionId)
        {
            RequireUiThread();
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            if (!_sessionsById.ContainsKey(sessionId))
            {
                return;
            }

            _tmuxService.StopWatching(sessionId);
            _tmuxService.Exit(sessionId);

            _sessionsById.Remove(sessionId);
            if (string.Equals(_frontSessionId, sessionId, StringComparison.Ordinal))
            {
                _frontSessionId = null;
            }

            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void HandleTmuxStateChanged(object? sender, TmuxSessionStateChangedEventArgs args)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                // 当前唯一允许的异步入口是 tmux 状态回调；修改 session 状态前必须先投递回 UI 线程。
                Dispatcher.UIThread.Post(() => HandleTmuxStateChanged(sender, args));
                return;
            }

            RequireUiThread();

            if (args.State == OngoingSessionState.Front)
            {
                return;
            }

            var changed = false;
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

            if (changed)
            {
                SessionsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private static void RequireUiThread()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                throw new InvalidOperationException(
                    "SessionLifecycleService must be called from the UI thread.");
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
