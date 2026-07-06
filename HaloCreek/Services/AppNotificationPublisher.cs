using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HaloCreek.Models;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Services.SessionState;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services
{
    public sealed class AppNotificationPublisher : IDisposable
    {
        private const string LogCategory = "Notification";

        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly UserNotificationPlatform _userNotificationPlatform;
        private readonly Func<bool> _applicationWindowHasFocus;
        private readonly Dictionary<string, TrackedSession> _trackedSessionsById = new(StringComparer.Ordinal);
        private readonly HashSet<string> _completedSessionIds = new(StringComparer.Ordinal);
        private bool _isDisposed;

        public AppNotificationPublisher(
            SessionLifecycleService sessionLifecycleService,
            UserNotificationPlatform userNotificationPlatform,
            Func<bool> applicationWindowHasFocus)
        {
            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
            _userNotificationPlatform = userNotificationPlatform
                ?? throw new ArgumentNullException(nameof(userNotificationPlatform));
            _applicationWindowHasFocus = applicationWindowHasFocus
                ?? throw new ArgumentNullException(nameof(applicationWindowHasFocus));

            _sessionLifecycleService.SessionsChanged += OnSessionsChanged;
            SyncTrackedSessions();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _sessionLifecycleService.SessionsChanged -= OnSessionsChanged;
            foreach (var trackedSession in _trackedSessionsById.Values)
            {
                trackedSession.StateSnapshots.Changed -= trackedSession.StateChangedHandler;
            }

            _trackedSessionsById.Clear();
            _completedSessionIds.Clear();
        }

        private void OnSessionsChanged(object? sender, EventArgs e)
        {
            if (_isDisposed)
            {
                return;
            }

            SyncTrackedSessions();
        }

        private void SyncTrackedSessions()
        {
            var sessions = _sessionLifecycleService.GetOngoingSessionInfos();
            var liveSessionIds = sessions
                .Select(session => session.Id)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var staleSessionId in _trackedSessionsById.Keys
                .Where(sessionId => !liveSessionIds.Contains(sessionId))
                .ToArray())
            {
                StopTrackingSession(staleSessionId);
            }

            _completedSessionIds.RemoveWhere(sessionId => !liveSessionIds.Contains(sessionId));

            foreach (var session in sessions)
            {
                TrackSession(session);
            }
        }

        private void TrackSession(OngoingSessionInfo session)
        {
            if (session.StateSnapshots is null)
            {
                StopTrackingSession(session.Id);
                return;
            }

            if (_trackedSessionsById.TryGetValue(session.Id, out var existing)
                && ReferenceEquals(existing.StateSnapshots, session.StateSnapshots))
            {
                existing.Session = session;
                EvaluateSessionState(existing);
                return;
            }

            StopTrackingSession(session.Id);

            EventHandler handler = (_, _) => OnSessionSnapshotChanged(session.Id);
            var trackedSession = new TrackedSession(session, session.StateSnapshots, handler);
            session.StateSnapshots.Changed += handler;
            _trackedSessionsById.Add(session.Id, trackedSession);

            EvaluateSessionState(trackedSession);
        }

        private void StopTrackingSession(string sessionId)
        {
            if (!_trackedSessionsById.Remove(sessionId, out var trackedSession))
            {
                return;
            }

            trackedSession.StateSnapshots.Changed -= trackedSession.StateChangedHandler;
        }

        private void OnSessionSnapshotChanged(string sessionId)
        {
            if (_isDisposed
                || !_trackedSessionsById.TryGetValue(sessionId, out var trackedSession))
            {
                return;
            }

            EvaluateSessionState(trackedSession);
        }

        private void EvaluateSessionState(TrackedSession trackedSession)
        {
            if (trackedSession.StateSnapshots.Current.State != SessionTaskState.TaskCompleted
                || !_completedSessionIds.Add(trackedSession.Session.Id)
                || _applicationWindowHasFocus())
            {
                return;
            }

            _ = SendSessionCompletedNotificationAsync(trackedSession.Session);
        }

        private async Task SendSessionCompletedNotificationAsync(OngoingSessionInfo session)
        {
            try
            {
                await _userNotificationPlatform.SendAsync(new UserNotificationRequest(
                    "Codex session completed",
                    session.Title));
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, $"Session completed notification failed. Error={ex.Message}");
                Log.Debug(LogCategory, ex.ToString());
            }
        }

        private sealed class TrackedSession
        {
            public TrackedSession(
                OngoingSessionInfo session,
                IWorkspaceSnapshotSource<SessionStateSnapshot> stateSnapshots,
                EventHandler stateChangedHandler)
            {
                Session = session ?? throw new ArgumentNullException(nameof(session));
                StateSnapshots = stateSnapshots ?? throw new ArgumentNullException(nameof(stateSnapshots));
                StateChangedHandler = stateChangedHandler ?? throw new ArgumentNullException(nameof(stateChangedHandler));
            }

            public OngoingSessionInfo Session { get; set; }

            public IWorkspaceSnapshotSource<SessionStateSnapshot> StateSnapshots { get; }

            public EventHandler StateChangedHandler { get; }
        }
    }
}
