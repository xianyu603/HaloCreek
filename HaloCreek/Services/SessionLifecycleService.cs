using Avalonia.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services.SessionKeepAlive;
using HaloCreek.Services.SessionState;
using HaloCreek.Services.WorkspaceSnapshots;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace HaloCreek.Services
{
    public sealed class SessionLifecycleService : IDisposable, INotifyPropertyChanged
    {
        private const string LogCategory = "SessionState";
        private static readonly TimeSpan CodexHistoryMatchPollInterval = TimeSpan.FromMilliseconds(250);

        // 正确性前提是所有共享状态的直接操作都来自同一个 UI 线程。
        private readonly Dictionary<string, OngoingSession> _sessionsById = new(StringComparer.Ordinal);
        private readonly ObservableCollection<OngoingSession> _allSessions = [];
        private readonly Dictionary<string, WorkspaceSnapshotStore<SessionStateSnapshot>> _sessionStateStoresById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, WorkspaceSnapshotStore<SessionKeepAliveSnapshot>> _sessionKeepAliveStoresById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CancellationTokenSource> _codexSessionIdWaitsById = new(StringComparer.Ordinal);
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
            DisposeSessionKeepAliveStores();
            CancelCodexSessionIdWaits();
            _sessionsById.Clear();
            _allSessions.Clear();
            _frontSessionId = null;
            SetFrontSession(null);
        }

        public Task<OngoingSession> LaunchAsync(string promptText)
        {
            RequireUiThread();

            if (string.IsNullOrWhiteSpace(promptText))
            {
                throw new InvalidOperationException("Prompt is empty.");
            }

            return StartCodexSessionAsync(
                new[] { promptText },
                FirstPromptSummaryFormatter.Format(promptText),
                promptText,
                new SessionRestartSource.LaunchPrompt(promptText),
                knownCodexSessionId: null);
        }

        public Task<OngoingSession> ResumeAsync(HistorySessionInfo? session)
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
                FirstPromptSummaryFormatter.Format(session.InitialPrompt),
                historyPromptText: null,
                new SessionRestartSource.CodexSession(session.Id),
                knownCodexSessionId: session.Id);
        }

        public Task<OngoingSession> RestartAsync(OngoingSession? session)
        {
            RequireUiThread();

            if (session is null)
            {
                throw new InvalidOperationException("No session selected.");
            }

            if (!_sessionsById.TryGetValue(session.Id, out var currentSession))
            {
                throw new InvalidOperationException("Session is no longer available.");
            }

            if (!currentSession.CanRestart)
            {
                throw new InvalidOperationException("Session is not dead.");
            }

            var restartSource = currentSession.RestartSource;
            var titleSource = currentSession.Title;
            Exit(currentSession.Id);

            return restartSource switch
            {
                SessionRestartSource.CodexSession source => RestartCodexSessionAsync(source, titleSource),
                SessionRestartSource.LaunchPrompt source => LaunchAsync(source.PromptText),
                _ => throw new InvalidOperationException("Session restart source is unavailable."),
            };
        }

        private Task<OngoingSession> RestartCodexSessionAsync(
            SessionRestartSource.CodexSession source,
            string title)
        {
            if (string.IsNullOrWhiteSpace(source.SessionId))
            {
                throw new InvalidOperationException("Codex session id is empty.");
            }

            return StartCodexSessionAsync(
                new[] { "resume", source.SessionId },
                title,
                historyPromptText: null,
                new SessionRestartSource.CodexSession(source.SessionId),
                knownCodexSessionId: source.SessionId);
        }

        private async Task<OngoingSession> StartCodexSessionAsync(
            IReadOnlyList<string> codexArguments,
            string title,
            string? historyPromptText,
            SessionRestartSource restartSource,
            string? knownCodexSessionId)
        {
            RequireUiThread();
            ArgumentNullException.ThrowIfNull(codexArguments);
            ArgumentNullException.ThrowIfNull(restartSource);

            var workspace = WorkspaceRuntime.Current;
            var config = workspace.EffectiveConfig;
            if (string.IsNullOrWhiteSpace(config.CodexExecutableName))
            {
                throw new InvalidOperationException("Codex executable is not available.");
            }

            ArgumentNullException.ThrowIfNull(config.CodexLaunchArguments);

            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Codex session";
            }

            // 这个记录的是history.jsonl的快照
            var historySnapshot = string.IsNullOrWhiteSpace(knownCodexSessionId)
                ? CodexSessionFileLocator.CaptureHistorySnapshot()
                : null;

            var launchTask = _tmuxService.LaunchAsync(new TmuxLaunchRequest(
                workspace.WorkspacePath,
                config.CodexExecutableName,
                config.CodexLaunchArguments.Concat(codexArguments).ToArray(),
                title),
                out var identifier);

            var now = DateTimeOffset.Now;
            var session = new OngoingSession(
                identifier,
                title,
                restartSource,
                now,
                isFront: false,
                stateSnapshot: SessionStateSnapshot.CreateEmpty(),
                launchState: SessionLaunchState.Requested);
            AddSession(session);
            BringToFront(identifier);

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

                session.Set(launchState: SessionLaunchState.Launched);
                StartSessionKeepAliveStore(identifier);
                launchCompleted = true;

                if (!string.IsNullOrWhiteSpace(knownCodexSessionId))
                {
                    StartSessionStateStore(identifier, knownCodexSessionId.Trim());
                }
                else
                {
                    _ = WaitForCodexSessionIdAndStartStoreAsync(
                        identifier,
                        historySnapshot!,
                        historyPromptText);
                }
            }
            finally
            {
                if (!launchCompleted
                    && _sessionsById.ContainsKey(identifier))
                {
                    if (string.Equals(_frontSessionId, identifier, StringComparison.Ordinal))
                    {
                        _frontSessionId = null;
                    }

                    RemoveSession(identifier);
                }

                if (!launchCompleted)
                {
                    RemoveSessionStateStore(identifier);
                    RemoveSessionKeepAliveStore(identifier);
                    RemoveCodexSessionIdWait(identifier, true);
                }
            }

            return session;
        }

        public void BringToFront(string sessionId)
        {
            RequireUiThread();
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            if (!_sessionsById.ContainsKey(sessionId))
            {
                return;
            }

            if (string.Equals(_frontSessionId, sessionId, StringComparison.Ordinal)
                && _sessionsById.TryGetValue(sessionId, out var currentFrontSession)
                && currentFrontSession.IsFront)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_frontSessionId)
                && !string.Equals(_frontSessionId, sessionId, StringComparison.Ordinal)
                && _sessionsById.TryGetValue(_frontSessionId, out var currentPreviousFrontSession))
            {
                currentPreviousFrontSession.Set(isFront: false);
                _frontSessionId = null;
            }

            if (!_sessionsById.TryGetValue(sessionId, out var targetSession))
            {
                return;
            }

            targetSession.Set(isFront: true);
            _frontSessionId = sessionId;
            SetFrontSession(targetSession);
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
                || !frontSession.CanSendMessage)
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

            if (!_sessionsById.ContainsKey(sessionId))
            {
                return;
            }

            _tmuxService.Exit(sessionId);

            RemoveSessionStateStore(sessionId);
            RemoveSessionKeepAliveStore(sessionId);
            RemoveCodexSessionIdWait(sessionId, cancel: true);
            if (string.Equals(_frontSessionId, sessionId, StringComparison.Ordinal))
            {
                _frontSessionId = null;
            }

            RemoveSession(sessionId);
        }

        private bool TryGetFrontSessionId(out string frontSessionId)
        {
            if (!string.IsNullOrWhiteSpace(_frontSessionId)
                && _sessionsById.TryGetValue(_frontSessionId, out var frontSession)
                && frontSession.IsFront)
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

        private void StartSessionKeepAliveStore(string sessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            if (_isDisposed
                || !_sessionsById.TryGetValue(sessionId, out var session)
                || _sessionKeepAliveStoresById.ContainsKey(sessionId))
            {
                return;
            }

            var keepAliveSnapshots = WorkspaceSnapshotStore.Create<SessionKeepAliveSnapshot>(sessionId);
            keepAliveSnapshots.Changed += (_, _) =>
            {
                LogSessionKeepAliveSnapshotPublished(
                    sessionId,
                    keepAliveSnapshots.Current);
                PublishSessionKeepAliveSnapshotChanged(sessionId, keepAliveSnapshots.Current);
            };
            _sessionKeepAliveStoresById.Add(sessionId, keepAliveSnapshots);
            session.Set(keepAliveSnapshot: keepAliveSnapshots.Current);
        }

        private void StartSessionStateStore(
            string sessionId,
            string codexSessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(codexSessionId);

            if (_isDisposed
                || !_sessionsById.TryGetValue(sessionId, out var session)
                || _sessionStateStoresById.ContainsKey(sessionId))
            {
                return;
            }

            var stateSnapshots = WorkspaceSnapshotStore.Create<SessionStateSnapshot>(codexSessionId);
            stateSnapshots.Changed += (_, _) =>
            {
                LogSessionStateSnapshotPublished(
                    sessionId,
                    codexSessionId,
                    stateSnapshots.Current);
                PublishSessionStateSnapshotChanged(sessionId, stateSnapshots.Current);
            };
            _sessionStateStoresById.Add(sessionId, stateSnapshots);
            session.Set(
                launchState: SessionLaunchState.Started,
                restartSource: new SessionRestartSource.CodexSession(codexSessionId),
                stateSnapshot: stateSnapshots.Current);
        }

        private async Task WaitForCodexSessionIdAndStartStoreAsync(
            string sessionId,
            CodexHistorySnapshot historySnapshot,
            string? promptText)
        {
            if (string.IsNullOrWhiteSpace(promptText))
            {
                throw new InvalidOperationException(
                    "Codex history prompt text is required to match the launched session.");
            }

            var cancellationTokenSource = new CancellationTokenSource();
            _codexSessionIdWaitsById.Add(sessionId, cancellationTokenSource);
            try
            {
                var codexSessionId = await Task.Run(
                    () => WaitForCodexSessionId(
                        historySnapshot,
                        promptText,
                        cancellationTokenSource.Token),
                    cancellationTokenSource.Token);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RemoveCodexSessionIdWait(sessionId, cancel: false);

                    if (_isDisposed
                        || !_sessionsById.ContainsKey(sessionId))
                    {
                        return;
                    }

                    StartSessionStateStore(sessionId, codexSessionId);
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RemoveCodexSessionIdWait(sessionId, cancel: false);

                    if (_isDisposed
                        || !_sessionsById.ContainsKey(sessionId) 
                        || ex is OperationCanceledException)
                    {
                        return;
                    }

                    ReportFrontSessionFailure(
                        "Session state unavailable",
                        ex.Message,
                        ex);
                });
            }
            finally
            {
                cancellationTokenSource.Dispose();
            }
        }

        private void RemoveCodexSessionIdWait(
            string sessionId,
            bool cancel)
        {
            if (!_codexSessionIdWaitsById.Remove(sessionId, out var cancellationTokenSource))
            {
                return;
            }

            if (cancel)
            {
                cancellationTokenSource.Cancel();
            }
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

            session.Set(stateSnapshot: snapshot);
        }

        private void PublishSessionKeepAliveSnapshotChanged(
            string sessionId,
            SessionKeepAliveSnapshot snapshot)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => PublishSessionKeepAliveSnapshotChanged(sessionId, snapshot));
                return;
            }

            if (_isDisposed
                || !_sessionsById.TryGetValue(sessionId, out var session))
            {
                return;
            }

            session.Set(keepAliveSnapshot: snapshot);
        }

        private void AddSession(OngoingSession session)
        {
            _sessionsById.Add(session.Id, session);
            _allSessions.Add(session);
        }

        private void RemoveSession(string sessionId)
        {
            if (!_sessionsById.Remove(sessionId, out var session))
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

        private void SetFrontSession(OngoingSession? session)
        {
            if (ReferenceEquals(_frontSession, session))
            {
                return;
            }

            _frontSession = session;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FrontSession)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFrontSession)));
        }

        private void RemoveSessionStateStore(string sessionId)
        {
            if (!_sessionStateStoresById.Remove(sessionId, out var store))
            {
                return;
            }

            store.Dispose();
        }

        private void RemoveSessionKeepAliveStore(string sessionId)
        {
            if (!_sessionKeepAliveStoresById.Remove(sessionId, out var store))
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

        private void DisposeSessionKeepAliveStores()
        {
            foreach (var store in _sessionKeepAliveStoresById.Values)
            {
                store.Dispose();
            }

            _sessionKeepAliveStoresById.Clear();
        }

        private void CancelCodexSessionIdWaits()
        {
            foreach (var cancellationTokenSource in _codexSessionIdWaitsById.Values)
            {
                cancellationTokenSource.Cancel();
            }

            _codexSessionIdWaitsById.Clear();
        }

        private static string WaitForCodexSessionId(
            CodexHistorySnapshot historySnapshot,
            string promptText,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = CodexSessionFileLocator.FindNewHistoryEntry(
                    historySnapshot,
                    promptText);
                if (entry is not null)
                {
                    return entry.SessionId;
                }

                Thread.Sleep(CodexHistoryMatchPollInterval);
            }
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

        private static void LogSessionKeepAliveSnapshotPublished(
            string tmuxSessionId,
            SessionKeepAliveSnapshot snapshot)
        {
            Log.Info(
                LogCategory,
                "Ongoing session keep-alive snapshot published. "
                + $"TmuxSessionId={tmuxSessionId}, "
                + $"ExitCode={snapshot.ExitCode}");
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
