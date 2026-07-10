using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
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
        private readonly Dictionary<string, OngoingSession> _trackedSessionsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SessionTaskState> _taskStatesBySessionId = new(StringComparer.Ordinal);
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

            _userNotificationPlatform.Activated += OnNotificationActivated;
            ((INotifyCollectionChanged)_sessionLifecycleService.AllSessions).CollectionChanged +=
                OnSessionsChanged;
            foreach (var session in _sessionLifecycleService.AllSessions)
            {
                TrackSession(session);
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _userNotificationPlatform.Activated -= OnNotificationActivated;
            ((INotifyCollectionChanged)_sessionLifecycleService.AllSessions).CollectionChanged -=
                OnSessionsChanged;

            foreach (var session in _trackedSessionsById.Values.ToArray())
            {
                UntrackSession(session);
            }
        }

        private void OnNotificationActivated(
            object? sender,
            UserNotificationActivatedEventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnNotificationActivated(sender, e));
                return;
            }

            if (_isDisposed
                || string.IsNullOrWhiteSpace(e.Payload)
                || !_trackedSessionsById.ContainsKey(e.Payload))
            {
                return;
            }

            _sessionLifecycleService.BringToFront(e.Payload);
        }

        private void OnSessionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isDisposed)
            {
                return;
            }

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var session in _trackedSessionsById.Values.ToArray())
                {
                    UntrackSession(session);
                }

                foreach (var session in _sessionLifecycleService.AllSessions)
                {
                    TrackSession(session);
                }

                return;
            }

            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is OngoingSession session)
                    {
                        UntrackSession(session);
                    }
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is OngoingSession session)
                    {
                        TrackSession(session);
                    }
                }
            }
        }

        private void TrackSession(OngoingSession session)
        {
            if (!_trackedSessionsById.TryAdd(session.Id, session))
            {
                return;
            }

            session.PropertyChanged += OnSessionPropertyChanged;
            _taskStatesBySessionId[session.Id] = session.TaskState;
        }

        private void UntrackSession(OngoingSession session)
        {
            session.PropertyChanged -= OnSessionPropertyChanged;
            _trackedSessionsById.Remove(session.Id);
            _taskStatesBySessionId.Remove(session.Id);
        }

        private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isDisposed
                || e.PropertyName != nameof(OngoingSession.TaskState)
                || sender is not OngoingSession session)
            {
                return;
            }

            EvaluateSessionState(session);
        }

        private void EvaluateSessionState(OngoingSession session)
        {
            var previousTaskState = _taskStatesBySessionId.GetValueOrDefault(
                session.Id,
                SessionTaskState.Unknown);
            _taskStatesBySessionId[session.Id] = session.TaskState;

            // Notification follows the task-state edge. If snapshot refresh misses a
            // brief Completed -> Started -> Completed cycle, one notification may be missed;
            // that belongs in snapshot refresh/listen behavior, not notification state.
            if (session.TaskState != SessionTaskState.TaskCompleted
                || previousTaskState == SessionTaskState.TaskCompleted)
            {
                return;
            }

            if (_applicationWindowHasFocus())
            {
                return;
            }

            _ = SendSessionCompletedNotificationAsync(session);
        }

        private async Task SendSessionCompletedNotificationAsync(OngoingSession session)
        {
            try
            {
                var message = session.Messages.LastOrDefault()?.Message;
                if (string.IsNullOrWhiteSpace(message))
                {
                    Log.Warning(
                        LogCategory,
                        $"Session completed notification skipped because no session message is available. SessionId={session.Id}");
                    return;
                }

                await _userNotificationPlatform.SendAsync(new UserNotificationRequest(
                    session.Title,
                    message,
                    session.Id));
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, $"Session completed notification failed. Error={ex.Message}");
                Log.Debug(LogCategory, ex.ToString());
            }
        }
    }
}
