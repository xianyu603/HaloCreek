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

        public string StatusText => $"{(IsFront ? "Front" : "Background")} / {FormatActivity()}";

        public string ActivityText => FormatActivity();

        public string TaskStateText => FormatTaskState(TaskState);

        public string StateTimestampText => StateTimestamp is null
            ? "No state timestamp"
            : $"Updated {StateTimestamp.Value.ToLocalTime():HH:mm:ss}";

        public string TokenSummaryText => FormatTokenSummary(TokenInfo);

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

            if (SetProperty(ref _taskState, snapshot.State, nameof(TaskState)))
            {
                OnPropertyChanged(nameof(TaskStateText));
            }

            if (SetProperty(ref _stateTimestamp, snapshot.StateTimestamp, nameof(StateTimestamp)))
            {
                OnPropertyChanged(nameof(StateTimestampText));
            }

            SetProperty(ref _lastActiveTime, snapshot.LastActiveTime, nameof(LastActiveTime));
            if (SetProperty(ref _isActive, snapshot.Active, nameof(IsActive)))
            {
                statusTextChanged = true;
                OnPropertyChanged(nameof(ActivityText));
            }

            if (SetProperty(ref _tokenInfo, snapshot.TokenInfo, nameof(TokenInfo)))
            {
                OnPropertyChanged(nameof(TokenSummaryText));
            }

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

        private static string FormatTaskState(SessionTaskState state)
        {
            return state switch
            {
                SessionTaskState.TaskStarted => "Started",
                SessionTaskState.TaskAborted => "Aborted",
                SessionTaskState.TaskCompleted => "Completed",
                _ => "Unknown",
            };
        }

        private static string FormatTokenSummary(SessionTokenInfo? tokenInfo)
        {
            if (tokenInfo is null)
            {
                return "Token usage unavailable";
            }

            var total = tokenInfo.TotalTokenUsageTotal is null
                ? "total ?"
                : $"total {tokenInfo.TotalTokenUsageTotal.Value:N0}";
            var usedPercentage = tokenInfo.ContextWindow is null || tokenInfo.LastTokenUsageTotal is null
                ? "?"
                : $"{100 * tokenInfo.LastTokenUsageTotal.Value / tokenInfo.ContextWindow.Value:N0}";

            return $"context {usedPercentage}% used, {total}";
        }
    }
}
