using Avalonia.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services.SessionState;
using HaloCreek.Services.WorkspaceSnapshots;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaloCreek.Services
{
    public sealed class SessionLifecycleService : IDisposable
    {
        private const string LogCategory = "SessionState";

        // 正确性前提是所有共享状态的直接操作都来自同一个 UI 线程。
        private readonly Dictionary<string, OngoingSessionInfo> _sessionsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WorkspaceSnapshotStore<SessionStateSnapshot>> _sessionStateStoresById = new(StringComparer.Ordinal);
        private readonly TmuxService _tmuxService;
        private readonly TransientEventService _transientEventService;
        private string? _frontSessionId;
        private bool _isDisposed;

        public SessionLifecycleService(
            TmuxService tmuxService,
            AppCommonRuntime appCommonRuntime)
        {
            _tmuxService = tmuxService ?? throw new ArgumentNullException(nameof(tmuxService));
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

        public FrontSessionContext? GetFrontSessionContext()
        {
            RequireUiThread();

            if (!TryGetFrontSessionId(out var frontSessionId)
                || !_sessionsById.TryGetValue(frontSessionId, out var session)
                || !_sessionStateStoresById.TryGetValue(frontSessionId, out var stateSnapshots))
            {
                return null;
            }

            return new FrontSessionContext(session, stateSnapshots);
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

            DisposeSessionStateStores();
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
                promptText,
                promptText,
                knownCodexSessionId: null);
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
                session.InitialPrompt,
                historyPromptText: null,
                knownCodexSessionId: session.Id);
        }

        public IReadOnlyList<OngoingSessionInfo> GetOngoingSessionInfos()
        {
            return _sessionsById.Values
                .OrderBy(session => session.StartedAt)
                .ToArray();
        }

        private async Task<OngoingSessionInfo> StartCodexSessionAsync(
            IReadOnlyList<string> codexArguments,
            string titleSource,
            string? historyPromptText,
            string? knownCodexSessionId)
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
                title,
                historyPromptText,
                knownCodexSessionId),
                out var identifier);

            var now = DateTimeOffset.Now;
            var session = new OngoingSessionInfo(
                identifier,
                title,
                workspace.WorkspacePath,
                now,
                FrontSessionState.Background,
                TmuxHeartbeatState.Idle,
                IsInteractive: false);

            _sessionsById.Add(identifier, session);
            SessionsChanged?.Invoke(this, EventArgs.Empty);

            var launchCompleted = false;
            try
            {
                var launchResult = await launchTask;

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
                    IsInteractive = true
                };
                _sessionsById[identifier] = session;
                CreateSessionStateStore(session, launchResult.CodexSessionId);
                _tmuxService.StartWatching(identifier);
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

                if (!launchCompleted)
                {
                    RemoveSessionStateStore(identifier);
                }
            }

            return session;
        }

        public void BringToFront(string sessionId)
        {
            RequireUiThread();
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            string? previousFrontSessionId = null;
            if (!_sessionsById.TryGetValue(sessionId, out var session)
                || !session.IsInteractive)
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
                    FrontState = FrontSessionState.Background
                };
                _frontSessionId = null;
            }

            if (!_sessionsById.TryGetValue(sessionId, out var targetSession))
            {
                return;
            }

            _sessionsById[sessionId] = targetSession with
            {
                FrontState = FrontSessionState.Front
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
            RemoveSessionStateStore(sessionId);
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

            var changed = false;
            if (!_sessionsById.TryGetValue(args.Identifier, out var session))
            {
                return;
            }

            if (session.HeartbeatState != args.State)
            {
                _sessionsById[args.Identifier] = session with
                {
                    HeartbeatState = args.State
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
                && frontSession.FrontState == FrontSessionState.Front)
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

        private void CreateSessionStateStore(
            OngoingSessionInfo session,
            string codexSessionId)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentException.ThrowIfNullOrWhiteSpace(codexSessionId);

            var store = WorkspaceSnapshotStore.Create<SessionStateSnapshot>(codexSessionId);
            store.Changed += (_, _) => LogSessionStateSnapshotPublished(
                session.Id,
                codexSessionId,
                store.Current);
            _sessionStateStoresById.Add(session.Id, store);
        }

        private void RemoveSessionStateStore(string sessionId)
        {
            if (!_sessionStateStoresById.Remove(sessionId, out var store))
            {
                return;
            }

            store.Dispose();
        }

        private void DisposeSessionStateStores()
        {
            foreach (var store in _sessionStateStoresById.Values)
            {
                store.Dispose();
            }

            _sessionStateStoresById.Clear();
        }

        private static void LogSessionStateSnapshotPublished(
            string tmuxSessionId,
            string codexSessionId,
            SessionStateSnapshot snapshot)
        {
            Log.Info(
                LogCategory,
                "Ongoing session state snapshot published. "
                + $"TmuxSessionId={tmuxSessionId}, "
                + $"CodexSessionId={codexSessionId}, "
                + $"State={snapshot.State}, "
                + $"StateTimestamp={snapshot.StateTimestamp:O}, "
                + $"MessageCount={snapshot.Messages.Count}, "
                + $"ContextWindow={snapshot.TokenInfo?.ContextWindow}, "
                + $"TotalTokenUsageTotal={snapshot.TokenInfo?.TotalTokenUsageTotal}, "
                + $"LastTokenUsageTotal={snapshot.TokenInfo?.LastTokenUsageTotal}");
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

    public sealed record FrontSessionContext(
        OngoingSessionInfo Session,
        IWorkspaceSnapshotSource<SessionStateSnapshot> StateSnapshots);
}
