using Avalonia.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services.SessionState;
using HaloCreek.Services.WorkspaceSnapshots;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace HaloCreek.Services
{
    public sealed class SessionLifecycleService : IDisposable, INotifyPropertyChanged
    {
        private const string LogCategory = "SessionState";

        // 正确性前提是所有共享状态的直接操作都来自同一个 UI 线程。
        private readonly Dictionary<string, OngoingSessionInfo> _sessionsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, OngoingSession> _observableSessionsById = new(StringComparer.Ordinal);
        private readonly ObservableCollection<OngoingSession> _allSessions = [];
        private readonly Dictionary<string, WorkspaceSnapshotStore<SessionStateSnapshot>> _sessionStateStoresById = new(StringComparer.Ordinal);
        private readonly TmuxService _tmuxService;
        private readonly TransientEventService _transientEventService;
        private OngoingSession? _frontSession;
        private string? _frontSessionId;
        private bool _isDisposed;

        public SessionLifecycleService(
            TmuxService tmuxService,
            AppCommonRuntime appCommonRuntime)
        {
            _tmuxService = tmuxService ?? throw new ArgumentNullException(nameof(tmuxService));
            ArgumentNullException.ThrowIfNull(appCommonRuntime);
            _transientEventService = appCommonRuntime.TransientEventService;
            AllSessions = new ReadOnlyObservableCollection<OngoingSession>(_allSessions);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler<SessionLifecycleChangedEventArgs>? SessionsChanged;

        public OngoingSession? FrontSession
        {
            get
            {
                RequireUiThread();
                return _frontSession;
            }
        }

        public ReadOnlyObservableCollection<OngoingSession> AllSessions { get; }

        public bool HasFrontSession
        {
            get
            {
                RequireUiThread();
                return TryGetFrontSessionId(out _);
            }
        }

        public OngoingSessionInfo? GetFrontSessionInfo()
        {
            RequireUiThread();

            if (!TryGetFrontSessionId(out var frontSessionId)
                || !_sessionsById.TryGetValue(frontSessionId, out var session))
            {
                return null;
            }

            return session;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            var sessionIds = _sessionsById.Keys.ToArray();

            foreach (var sessionId in sessionIds)
            {
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
            _observableSessionsById.Clear();
            _allSessions.Clear();
            _frontSessionId = null;
            SetFrontSession(null);
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
                SessionStateSnapshot.CreateEmpty(),
                IsInteractive: false);

            _sessionsById.Add(identifier, session);
            var observableSession = new OngoingSession(
                identifier,
                title,
                now,
                isFront: false,
                stateSnapshot: SessionStateSnapshot.CreateEmpty(),
                isInteractive: false);
            AddObservableSession(observableSession);
            PublishSessionsChanged(SessionLifecycleChangeKind.Added, session);
            BringToFront(identifier);

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

                var stateSnapshots = CreateSessionStateStore(session.Id, launchResult.CodexSessionId);
                var previousSession = session;
                session = session with
                {
                    StateSnapshot = stateSnapshots.Current,
                    IsInteractive = true
                };
                _sessionsById[identifier] = session;
                if (_observableSessionsById.TryGetValue(identifier, out observableSession))
                {
                    observableSession.Set(
                        isInteractive: true,
                        stateSnapshot: stateSnapshots.Current);
                }

                PublishSessionsChanged(
                    SessionLifecycleChangeKind.Updated,
                    session,
                    previousSession);

                launchCompleted = true;
            }
            finally
            {
                if (!launchCompleted
                    && _sessionsById.Remove(identifier, out var removedSession))
                {
                    if (string.Equals(_frontSessionId, identifier, StringComparison.Ordinal))
                    {
                        _frontSessionId = null;
                    }

                    RemoveObservableSession(identifier);
                    PublishSessionsChanged(SessionLifecycleChangeKind.Removed, removedSession);
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

            OngoingSessionInfo? previousFrontSession = null;
            if (!_sessionsById.ContainsKey(sessionId))
            {
                return;
            }

            if (string.Equals(_frontSessionId, sessionId, StringComparison.Ordinal)
                && _sessionsById.TryGetValue(sessionId, out var currentFrontSession)
                && currentFrontSession.FrontState == FrontSessionState.Front)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_frontSessionId)
                && !string.Equals(_frontSessionId, sessionId, StringComparison.Ordinal)
                && _sessionsById.TryGetValue(_frontSessionId, out var currentPreviousFrontSession))
            {
                var previousFrontSessionId = _frontSessionId;
                previousFrontSession = currentPreviousFrontSession with
                {
                    FrontState = FrontSessionState.Background
                };
                _sessionsById[previousFrontSessionId] = previousFrontSession;
                SetObservableSessionFront(previousFrontSessionId, false);
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
            SetObservableSessionFront(sessionId, true);
            _frontSessionId = sessionId;
            SetFrontSession(_observableSessionsById.GetValueOrDefault(sessionId));

            PublishSessionsChanged(
                SessionLifecycleChangeKind.FrontChanged,
                _sessionsById[sessionId],
                previousFrontSession);
        }

        public async Task SendMessageToFrontSessionAsync(string message)
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

            if (!_sessionsById.TryGetValue(frontSessionId, out var frontSession)
                || !frontSession.IsInteractive)
            {
                ReportFrontSessionFailure("Send to front failed", "Front session is still starting.");
                return;
            }

            try
            {
                await _tmuxService.SendMessageToSessionAsync(frontSessionId, message);
            }
            catch (InvalidOperationException ex)
            {
                ReportFrontSessionFailure("Send to front failed", ex.Message, ex);
            }
        }

        public void Exit(string sessionId)
        {
            RequireUiThread();
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            if (!_sessionsById.TryGetValue(sessionId, out var removedSession))
            {
                return;
            }

            _tmuxService.Exit(sessionId);

            _sessionsById.Remove(sessionId);
            RemoveSessionStateStore(sessionId);
            if (string.Equals(_frontSessionId, sessionId, StringComparison.Ordinal))
            {
                _frontSessionId = null;
            }

            RemoveObservableSession(sessionId);
            PublishSessionsChanged(SessionLifecycleChangeKind.Removed, removedSession);
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

        private void ReportFrontSessionFailure(
            string title,
            string message,
            Exception? exception = null)
        {
            _transientEventService.ReportUserActionFailure(
                "SessionLifecycle",
                title,
                message,
                exception);
        }

        private WorkspaceSnapshotStore<SessionStateSnapshot> CreateSessionStateStore(
            string sessionId,
            string codexSessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(codexSessionId);

            var store = WorkspaceSnapshotStore.Create<SessionStateSnapshot>(codexSessionId);
            store.Changed += (_, _) =>
            {
                LogSessionStateSnapshotPublished(
                    sessionId,
                    codexSessionId,
                    store.Current);
                PublishSessionStateSnapshotChanged(sessionId, store.Current);
            };
            _sessionStateStoresById.Add(sessionId, store);
            return store;
        }

        private void PublishSessionStateSnapshotChanged(
            string sessionId,
            SessionStateSnapshot snapshot)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => PublishSessionStateSnapshotChanged(sessionId, snapshot));
                return;
            }

            if (_isDisposed
                || !_sessionsById.TryGetValue(sessionId, out var session))
            {
                return;
            }

            var updatedSession = session with
            {
                StateSnapshot = snapshot
            };
            _sessionsById[sessionId] = updatedSession;
            if (_observableSessionsById.TryGetValue(sessionId, out var observableSession))
            {
                observableSession.Set(stateSnapshot: snapshot);
            }

            PublishSessionsChanged(
                SessionLifecycleChangeKind.StateSnapshotChanged,
                updatedSession,
                session);
        }

        private void PublishSessionsChanged(
            SessionLifecycleChangeKind changeKind,
            OngoingSessionInfo? session,
            OngoingSessionInfo? previousSession = null)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => PublishSessionsChanged(changeKind, session, previousSession));
                return;
            }

            if (_isDisposed)
            {
                return;
            }

            SessionsChanged?.Invoke(
                this,
                new SessionLifecycleChangedEventArgs(changeKind, session, previousSession));
        }

        private void AddObservableSession(OngoingSession session)
        {
            _observableSessionsById.Add(session.Id, session);
            _allSessions.Add(session);
        }

        private void RemoveObservableSession(string sessionId)
        {
            if (!_observableSessionsById.Remove(sessionId, out var session))
            {
                return;
            }

            session.Set(isFront: false);
            if (ReferenceEquals(_frontSession, session))
            {
                SetFrontSession(null);
            }

            _allSessions.Remove(session);
        }

        private void SetObservableSessionFront(string sessionId, bool isFront)
        {
            if (_observableSessionsById.TryGetValue(sessionId, out var session))
            {
                session.Set(isFront: isFront);
            }
        }

        private void SetFrontSession(OngoingSession? session)
        {
            if (ReferenceEquals(_frontSession, session))
            {
                return;
            }

            _frontSession = session;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FrontSession)));
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

}
