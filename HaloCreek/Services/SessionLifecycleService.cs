using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class SessionLifecycleService : IDisposable
    {
        private const int FirstPromptSummaryMaxLength = 20;

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
            string codexExecutableName,
            IReadOnlyList<string> codexLaunchArguments)
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

            if (string.IsNullOrWhiteSpace(codexExecutableName))
            {
                return new SessionLaunchResult(false, "Codex executable is not available.", null);
            }

            ArgumentNullException.ThrowIfNull(codexLaunchArguments);

            var title = BuildFirstPromptSummary(promptText);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Codex session";
            }

            string identifier;
            try
            {
                identifier = _tmuxService.Launch(new TmuxLaunchRequest(
                    workspacePath,
                    codexExecutableName,
                    codexLaunchArguments.Concat(new[] { promptText }).ToArray(),
                    title));
            }
            catch (InvalidOperationException ex)
            {
                return new SessionLaunchResult(false, ex.Message, null);
            }

            var now = DateTimeOffset.Now;
            var session = new OngoingSessionInfo(
                identifier,
                title,
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
            string codexExecutableName,
            IReadOnlyList<string> codexLaunchArguments)
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

            if (string.IsNullOrWhiteSpace(codexExecutableName))
            {
                return new SessionResumeResult(false, "Codex executable is not available.");
            }

            ArgumentNullException.ThrowIfNull(codexLaunchArguments);

            var title = BuildFirstPromptSummary(session.InitialPrompt);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Codex resume session";
            }

            string identifier;
            try
            {
                identifier = _tmuxService.Launch(new TmuxLaunchRequest(
                    currentWorkspacePath,
                    codexExecutableName,
                    codexLaunchArguments.Concat(new[] { "resume", session.Id }).ToArray(),
                    title));
            }
            catch (InvalidOperationException ex)
            {
                return new SessionResumeResult(false, ex.Message);
            }

            var now = DateTimeOffset.Now;
            var ongoingSession = new OngoingSessionInfo(
                identifier,
                title,
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

        public FrontSessionSendResult SendMessageToFrontSession(string message)
        {
            RequireUiThread();
            ArgumentNullException.ThrowIfNull(message);

            if (string.IsNullOrWhiteSpace(_frontSessionId)
                || !_sessionsById.TryGetValue(_frontSessionId, out var frontSession)
                || frontSession.State != OngoingSessionState.Front)
            {
                return new FrontSessionSendResult(false, "No front session is available.");
            }

            var result = _tmuxService.SendMessageToFrontSession(message);
            return new FrontSessionSendResult(result.Sent, result.StatusMessage);
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

        private static string BuildFirstPromptSummary(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var summary = string.Join(
                " ",
                text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (summary.Length > FirstPromptSummaryMaxLength)
            {
                summary = summary[..FirstPromptSummaryMaxLength];
            }

            return summary;
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

    public sealed record FrontSessionSendResult(
        bool Sent,
        string StatusMessage);
}
