using Avalonia.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace HaloCreek.Services
{
    public sealed class SessionLifecycleService : IDisposable
    {
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

        public Task<OngoingSessionInfo> LaunchAsync(string promptText)
        {
            RequireUiThread();

            if (string.IsNullOrWhiteSpace(promptText))
            {
                throw new InvalidOperationException("Prompt is empty.");
            }

            return StartCodexSessionAsync(
                new[] { promptText },
                promptText);
        }

        public Task<OngoingSessionInfo> ResumeAsync(HistorySessionInfo? session)
        {
            RequireUiThread();

            if (session is null)
            {
                throw new InvalidOperationException("No session selected.");
            }

            if (string.IsNullOrWhiteSpace(session.Id))
            {
                throw new InvalidOperationException("Session id is empty.");
            }

            return StartCodexSessionAsync(
                new[] { "resume", session.Id },
                session.InitialPrompt);
        }

        public IReadOnlyList<OngoingSessionInfo> GetOngoingSessionInfos()
        {
            return _sessionsById.Values
                .OrderBy(session => session.StartedAt)
                .ToArray();
        }

        private async Task<OngoingSessionInfo> StartCodexSessionAsync(
            IReadOnlyList<string> codexArguments,
            string titleSource)
        {
            RequireUiThread();
            ArgumentNullException.ThrowIfNull(codexArguments);

            var workspace = WorkspaceRuntime.Current;
            var config = workspace.EffectiveConfig;
            if (string.IsNullOrWhiteSpace(config.CodexExecutableName))
            {
                throw new InvalidOperationException("Codex executable is not available.");
            }

            ArgumentNullException.ThrowIfNull(config.CodexLaunchArguments);

            var title = FirstPromptSummaryFormatter.Format(titleSource);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Codex session";
            }

            var launchTask = _tmuxService.LaunchAsync(new TmuxLaunchRequest(
                workspace.WorkspacePath,
                config.CodexExecutableName,
                config.CodexLaunchArguments.Concat(codexArguments).ToArray(),
                title),
                out var identifier);

            var now = DateTimeOffset.Now;
            var session = new OngoingSessionInfo(
                identifier,
                title,
                workspace.WorkspacePath,
                now,
                OngoingSessionState.Launching);

            _sessionsById.Add(identifier, session);
            SessionsChanged?.Invoke(this, EventArgs.Empty);

            var launchCompleted = false;
            try
            {
                await launchTask;

                if (_isDisposed)
                {
                    _tmuxService.Exit(identifier);
                    throw new InvalidOperationException("Application is closing.");
                }

                if (!_sessionsById.TryGetValue(identifier, out session))
                {
                    _tmuxService.Exit(identifier);
                    throw new InvalidOperationException("Session closed before launch completed.");
                }

                session = session with
                {
                    State = OngoingSessionState.BackgroundRunning
                };
                _sessionsById[identifier] = session;
                SessionsChanged?.Invoke(this, EventArgs.Empty);

                launchCompleted = true;
            }
            finally
            {
                if (!launchCompleted
                    && _sessionsById.Remove(identifier))
                {
                    if (string.Equals(_frontSessionId, identifier, StringComparison.Ordinal))
                    {
                        _frontSessionId = null;
                    }

                    SessionsChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            // TODO 如果卡顿把这里的BringToFront也改成异步
            BringToFront(identifier);

            return session;
        }

        public void BringToFront(string sessionId)
        {
            RequireUiThread();
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            string? previousFrontSessionId = null;
            if (!_sessionsById.TryGetValue(sessionId, out var session)
                || session.State == OngoingSessionState.Launching)
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

            // 这里故意先由 SessionLifecycleService 编排 terminal 与 tmux：
            // 当前只有 BringToFront 这一条调用路径需要同时处理 WT 窗口和 tmux 前台 client，
            // 抽出独立“前台客户端”服务会增加一层暂时没有复用收益的间接调用。
            // 如果后续出现多处复用、复杂失败恢复，或 terminal/tmux 开始互相持有对方状态，
            // 再把这段收敛成独立的前台呈现服务。
            if (_tmuxService.HasFrontClient())
            {
                _terminalService.ActivateFrontClient();
                _tmuxService.SwitchFrontClient(sessionId);
            }
            else
            {
                var startupCommand = _tmuxService.GetFrontClientStartupCommand(sessionId);
                _terminalService.LaunchFrontClient(startupCommand);
                _tmuxService.MarkFrontClientAttachedToSession(sessionId);
            }

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

            if (!TryGetFrontSessionId(out var frontSessionId))
            {
                // TODO 此处异常处理应当由业务决定
                ReportFrontSessionFailure("Activate front session failed", "No front session is available.");
                return;
            }

            BringToFront(frontSessionId);
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

}
