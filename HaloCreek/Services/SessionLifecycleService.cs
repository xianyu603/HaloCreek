using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly TransientEventService _transientEventService;
        private string? _frontSessionId;
        private bool _isDisposed;

        public SessionLifecycleService(
            TmuxService tmuxService,
            TerminalService terminalService,
            AppCommonRuntime appCommonRuntime)
        {
            _tmuxService = tmuxService ?? throw new ArgumentNullException(nameof(tmuxService));
            _terminalService = terminalService ?? throw new ArgumentNullException(nameof(terminalService));
            ArgumentNullException.ThrowIfNull(appCommonRuntime);
            _transientEventService = appCommonRuntime.TransientEventService;
            _tmuxService.StateChanged += HandleTmuxStateChanged;
            WorkspaceRuntime.Changed += HandleWorkspaceChanged;
        }

        // 只向外部汇报session或其状态发生了变化 先不实现复杂的消息通知 卡了再优化
        public event EventHandler? SessionsChanged;

        public bool HasFrontSession
        {
            get
            {
                RequireUiThread();
                return TryGetFrontSessionId(out _);
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            // TODO: LaunchAsync/ResumeAsync may still be waiting for tmux launch.
            // If the app exits while tmux is creating a session, that session can be
            // created after this service has already collected _sessionsById and
            // become an orphan. Handle pending launch/resume cleanup separately.
            _isDisposed = true;
            _tmuxService.StateChanged -= HandleTmuxStateChanged;
            WorkspaceRuntime.Changed -= HandleWorkspaceChanged;

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
        public async Task<SessionLaunchResult> LaunchAsync(string promptText)
        {
            RequireUiThread();

            if (string.IsNullOrWhiteSpace(promptText))
            {
                return new SessionLaunchResult(false, "Prompt is empty.", null);
            }

            var workspace = WorkspaceRuntime.Current;
            var config = workspace.EffectiveConfig;
            if (string.IsNullOrWhiteSpace(config.CodexExecutableName))
            {
                return new SessionLaunchResult(false, "Codex executable is not available.", null);
            }

            ArgumentNullException.ThrowIfNull(config.CodexLaunchArguments);

            var title = BuildFirstPromptSummary(promptText);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Codex session";
            }

            string identifier;
            try
            {
                identifier = await _tmuxService.LaunchAsync(new TmuxLaunchRequest(
                    workspace.WorkspacePath,
                    config.CodexExecutableName,
                    config.CodexLaunchArguments.Concat(new[] { promptText }).ToArray(),
                    title));
            }
            catch (InvalidOperationException ex)
            {
                return new SessionLaunchResult(false, ex.Message, null);
            }

            if (_isDisposed)
            {
                _tmuxService.Exit(identifier);
                return new SessionLaunchResult(false, "Application is closing.", null);
            }

            if (!string.Equals(
                    WorkspaceRuntime.Current.WorkspacePath,
                    workspace.WorkspacePath,
                    StringComparison.Ordinal))
            {
                _tmuxService.Exit(identifier);
                return new SessionLaunchResult(false, "Workspace changed before launch completed.", null);
            }

            var now = DateTimeOffset.Now;
            var session = new OngoingSessionInfo(
                identifier,
                title,
                workspace.WorkspacePath,
                now,
                OngoingSessionState.BackgroundRunning);

            _sessionsById.Add(identifier, session);

            // TODO 如果卡顿把这里的BringToFront也改成异步
            BringToFront(identifier);

            return new SessionLaunchResult(true, "Codex session launch requested.", session);
        }

        public async Task<SessionResumeResult> ResumeAsync(HistorySessionInfo? session)
        {
            RequireUiThread();

            if (session is null)
            {
                return new SessionResumeResult(false, "No session selected.");
            }

            if (string.IsNullOrWhiteSpace(session.Id))
            {
                return new SessionResumeResult(false, "Session id is empty.");
            }

            var workspace = WorkspaceRuntime.Current;
            var config = workspace.EffectiveConfig;
            if (string.IsNullOrWhiteSpace(config.CodexExecutableName))
            {
                return new SessionResumeResult(false, "Codex executable is not available.");
            }

            ArgumentNullException.ThrowIfNull(config.CodexLaunchArguments);

            var title = BuildFirstPromptSummary(session.InitialPrompt);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Codex resume session";
            }

            string identifier;
            try
            {
                identifier = await _tmuxService.LaunchAsync(new TmuxLaunchRequest(
                    workspace.WorkspacePath,
                    config.CodexExecutableName,
                    config.CodexLaunchArguments.Concat(new[] { "resume", session.Id }).ToArray(),
                    title));
            }
            catch (InvalidOperationException ex)
            {
                return new SessionResumeResult(false, ex.Message);
            }

            if (_isDisposed)
            {
                _tmuxService.Exit(identifier);
                return new SessionResumeResult(false, "Application is closing.");
            }

            if (!string.Equals(
                    WorkspaceRuntime.Current.WorkspacePath,
                    workspace.WorkspacePath,
                    StringComparison.Ordinal))
            {
                _tmuxService.Exit(identifier);
                return new SessionResumeResult(false, "Workspace changed before resume completed.");
            }

            var now = DateTimeOffset.Now;
            var ongoingSession = new OngoingSessionInfo(
                identifier,
                title,
                workspace.WorkspacePath,
                now,
                OngoingSessionState.BackgroundRunning);

            _sessionsById.Add(identifier, ongoingSession);

            BringToFront(identifier);

            return new SessionResumeResult(true, "Codex session resume requested.");
        }

        public IReadOnlyList<OngoingSessionInfo> GetCurrentWorkspaceOngoingSessions()
        {
            RequireUiThread();

            return GetOngoingSessionsForWorkspace(WorkspaceRuntime.Current.WorkspacePath);
        }

        private IReadOnlyList<OngoingSessionInfo> GetOngoingSessionsForWorkspace(string workspacePath)
        {
            RequireUiThread();
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            return _sessionsById.Values
                .Where(session =>
                    string.Equals(session.WorkspacePath, workspacePath, StringComparison.Ordinal))
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

        public void SendMessageToFrontSession(string message)
        {
            RequireUiThread();
            ArgumentNullException.ThrowIfNull(message);

            if (string.IsNullOrWhiteSpace(message))
            {
                ReportFrontSessionFailure("Send to front failed", "Message is empty.");
                return;
            }

            if (!TryGetFrontSessionId(out var frontSessionId))
            {
                ReportFrontSessionFailure("Send to front failed", "No front session is available.");
                return;
            }

            _tmuxService.SendMessageToSession(frontSessionId, message);
        }

        public void ActivateFrontClient()
        {
            RequireUiThread();

            if (!TryGetFrontSessionId(out _))
            {
                ReportFrontSessionFailure("Activate front session failed", "No front session is available.");
                return;
            }

            _terminalService.ActivateFrontClient();
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

        private void HandleWorkspaceChanged(WorkspaceContext workspace)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => HandleWorkspaceChanged(workspace));
                return;
            }

            if (_isDisposed)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(workspace);

            var sessionIds = _sessionsById.Values
                .Where(session =>
                    !string.Equals(session.WorkspacePath, workspace.WorkspacePath, StringComparison.Ordinal))
                .Select(session => session.Id)
                .ToArray();

            foreach (var sessionId in sessionIds)
            {
                Exit(sessionId);
            }
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

        private bool TryGetFrontSessionId(out string frontSessionId)
        {
            if (!string.IsNullOrWhiteSpace(_frontSessionId)
                && _sessionsById.TryGetValue(_frontSessionId, out var frontSession)
                && frontSession.State == OngoingSessionState.Front)
            {
                frontSessionId = _frontSessionId;
                return true;
            }

            frontSessionId = string.Empty;
            return false;
        }

        private void ReportFrontSessionFailure(string title, string message)
        {
            _transientEventService.ReportUserActionFailure(
                "SessionLifecycle",
                title,
                message);
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
