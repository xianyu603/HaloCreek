using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using HaloCreek.Services.SessionState;

namespace HaloCreek.Models
{
    public sealed class OngoingSession : ObservableObject
    {
        private bool _isFront;
        private bool _isInteractive;
        private SessionTaskState _taskState;
        private DateTimeOffset? _stateTimestamp;
        private DateTimeOffset? _lastActiveTime;
        private bool? _isActive;
        private IReadOnlyList<SessionMessage> _messages = Array.Empty<SessionMessage>();
        private SessionTokenInfo? _tokenInfo;

        internal OngoingSession(
            string id,
            string title,
            DateTimeOffset startedAt,
            bool isFront,
            SessionStateSnapshot stateSnapshot,
            bool isInteractive)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(stateSnapshot);

            Id = id;
            Title = title;
            StartedAt = startedAt;
            Set(
                isFront: isFront,
                isInteractive: isInteractive,
                stateSnapshot: stateSnapshot);
        }

        public string Id { get; }

        public string Title { get; }

        public DateTimeOffset StartedAt { get; }

        public bool IsFront => _isFront;

        public bool IsInteractive => _isInteractive;

        public SessionTaskState TaskState => _taskState;

        public DateTimeOffset? StateTimestamp => _stateTimestamp;

        public DateTimeOffset? LastActiveTime => _lastActiveTime;

        public bool? IsActive => _isActive;

        public IReadOnlyList<SessionMessage> Messages => _messages;

        public SessionTokenInfo? TokenInfo => _tokenInfo;

        public string StatusText => $"{(IsFront ? FrontSessionState.Front : FrontSessionState.Background)} / {FormatActivity()}";

        // This is intentionally internal and should only be called by SessionLifecycleService.
        // Keep it direct so reference search shows every lifecycle update point.
        internal void Set(
            bool? isFront = null,
            bool? isInteractive = null,
            SessionStateSnapshot? stateSnapshot = null)
        {
            var statusTextChanged = false;

            if (isFront is { } nextIsFront // isFront有值表达式为true 并将值扔到nextXX
                && SetProperty(ref _isFront, nextIsFront, nameof(IsFront)))
            {
                statusTextChanged = true;
            }

            if (isInteractive is { } nextIsInteractive)
            {
                SetProperty(ref _isInteractive, nextIsInteractive, nameof(IsInteractive));
            }

            if (stateSnapshot is not null)
            {
                SetState(stateSnapshot, out var statusTextChangedBySnapshot);
                statusTextChanged |= statusTextChangedBySnapshot;
            }

            if (statusTextChanged)
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }

        private void SetState(SessionStateSnapshot snapshot, out bool statusTextChanged)
        {
            statusTextChanged = false;

            SetProperty(ref _taskState, snapshot.State, nameof(TaskState));
            SetProperty(ref _stateTimestamp, snapshot.StateTimestamp, nameof(StateTimestamp));
            SetProperty(ref _lastActiveTime, snapshot.LastActiveTime, nameof(LastActiveTime));
            if (SetProperty(ref _isActive, snapshot.Active, nameof(IsActive)))
            {
                statusTextChanged = true;
            }

            SetProperty(ref _tokenInfo, snapshot.TokenInfo, nameof(TokenInfo));

            if (!_messages.SequenceEqual(snapshot.Messages))
            {
                _messages = snapshot.Messages.ToArray();
                OnPropertyChanged(nameof(Messages));
            }
        }

        private string FormatActivity()
        {
            return IsActive switch
            {
                true => "Active",
                false => "Idle",
                _ => "Unknown",
            };
        }
    }
}
