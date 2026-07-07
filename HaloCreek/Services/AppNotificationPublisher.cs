using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HaloCreek.Models;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Services.SessionState;

namespace HaloCreek.Services
{
    public sealed class AppNotificationPublisher : IDisposable
    {
        private const string LogCategory = "Notification";

        private readonly SessionLifecycleService _sessionLifecycleService;
        private readonly UserNotificationPlatform _userNotificationPlatform;
        private readonly Func<bool> _applicationWindowHasFocus;
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
            foreach (var session in _sessionLifecycleService.GetOngoingSessionInfos())
            {
                EvaluateSessionState(session);
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _sessionLifecycleService.SessionsChanged -= OnSessionsChanged;

            _completedSessionIds.Clear();
        }

        private void OnSessionsChanged(object? sender, SessionLifecycleChangedEventArgs e)
        {
            if (_isDisposed)
            {
                return;
            }

            if (e.ChangeKind == SessionLifecycleChangeKind.Removed)
            {
                if (e.Session is not null)
                {
                    _completedSessionIds.Remove(e.Session.Id);
                }
                return;
            }

            if (e.Session is null)
            {
                return;
            }

            EvaluateSessionState(e.Session);
        }

        private void EvaluateSessionState(OngoingSessionInfo session)
        {
            if (session.StateSnapshot.State != SessionTaskState.TaskCompleted
                || !_completedSessionIds.Add(session.Id)
                || _applicationWindowHasFocus())
            {
                return;
            }

            _ = SendSessionCompletedNotificationAsync(session);
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
    }
}
