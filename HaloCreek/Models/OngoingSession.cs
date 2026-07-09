using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using HaloCreek.Services.SessionKeepAlive;
using HaloCreek.Services.SessionState;

namespace HaloCreek.Models
{
    public sealed class OngoingSession : ObservableObject
    {
        private bool _isFront;
        private SessionLaunchState _launchState;
        private SessionTaskState _taskState;
        private DateTimeOffset? _stateTimestamp;
        private DateTimeOffset? _lastActiveTime;
        private bool? _isActive;
        private IReadOnlyList<SessionMessage> _messages = Array.Empty<SessionMessage>();
        private SessionTokenInfo? _tokenInfo;
        private SessionKeepAliveStatus _sessionKeepAliveStatus = SessionKeepAliveStatus.Other;
        private SessionRestartSource _restartSource;

        internal OngoingSession(
            string id,
            string title,
            SessionRestartSource restartSource,
            DateTimeOffset startedAt,
            bool isFront,
            SessionStateSnapshot stateSnapshot,
            SessionLaunchState launchState)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(restartSource);
            ArgumentNullException.ThrowIfNull(stateSnapshot);

            Id = id;
            Title = title;
            _restartSource = restartSource;
            StartedAt = startedAt;
            Set(
                isFront: isFront,
                launchState: launchState,
                stateSnapshot: stateSnapshot);
        }

        public string Id { get; }

        public string Title { get; }

        public SessionRestartSource RestartSource => _restartSource;

        public DateTimeOffset StartedAt { get; }

        public bool IsFront => _isFront;

        public SessionLaunchState LaunchState => _launchState;

        public SessionKeepAliveStatus SessionKeepAliveStatus => _sessionKeepAliveStatus;

        public bool IsDead => SessionKeepAliveStatus == SessionKeepAliveStatus.Dead;

        public bool IsAliveOrUnknown => !IsDead;

        public bool IsKeepAliveOther => SessionKeepAliveStatus == SessionKeepAliveStatus.Other;

        public bool ShowNormalStatusBubbles => SessionKeepAliveStatus == SessionKeepAliveStatus.HasInputPane;

        public bool ShowAttentionStatusBubble => IsDead || IsKeepAliveOther;

        public bool CanOpenCli => IsAliveOrUnknown
            && LaunchState is SessionLaunchState.Launched or SessionLaunchState.Started;

        public bool CanRestart => IsDead;

        public bool CanSendMessage => LaunchState == SessionLaunchState.Started;

        public SessionTaskState TaskState => _taskState;

        public DateTimeOffset? StateTimestamp => _stateTimestamp;

        public DateTimeOffset? LastActiveTime => _lastActiveTime;

        public bool? IsActive => _isActive;

        public IReadOnlyList<SessionMessage> Messages => _messages;

        public SessionTokenInfo? TokenInfo => _tokenInfo;

        public string StatusText => $"{(IsFront ? "Front" : "Background")} / {FormatStatus()}";

        public string ActivityText => FormatActivity();

        public string TaskStateText => FormatTaskState(TaskState);

        public string StateTimestampText => IsDead
            ? "Process exited. Close or restart this session."
            : IsKeepAliveOther
            ? "Input pane was not detected. Open CLI to inspect this session."
            : StateTimestamp is null
            ? "No state timestamp"
            : $"Updated {StateTimestamp.Value.ToLocalTime():HH:mm:ss}";

        public string TokenSummaryText => FormatTokenSummary(TokenInfo);

        // This is intentionally internal and should only be called by SessionLifecycleService.
        // Keep it direct so reference search shows every lifecycle update point.
        internal void Set(
            bool? isFront = null,
            SessionLaunchState? launchState = null,
            SessionRestartSource? restartSource = null,
            SessionStateSnapshot? stateSnapshot = null,
            SessionKeepAliveSnapshot? keepAliveSnapshot = null)
        {
            var statusTextChanged = false;

            if (isFront is { } nextIsFront // isFront有值表达式为true 并将值扔到nextXX
                && SetProperty(ref _isFront, nextIsFront, nameof(IsFront)))
            {
                statusTextChanged = true;
            }

            if (launchState is { } nextLaunchState
                && SetProperty(ref _launchState, nextLaunchState, nameof(LaunchState)))
            {
                statusTextChanged = true;
                OnPropertyChanged(nameof(CanOpenCli));
                OnPropertyChanged(nameof(CanSendMessage));
            }

            if (restartSource is { } nextRestartSource)
            {
                SetProperty(ref _restartSource, nextRestartSource, nameof(RestartSource));
            }

            if (stateSnapshot is not null)
            {
                SetState(stateSnapshot, out var statusTextChangedBySnapshot);
                statusTextChanged |= statusTextChangedBySnapshot;
            }

            if (keepAliveSnapshot is not null)
            {
                SetKeepAlive(keepAliveSnapshot, out var statusTextChangedByKeepAlive);
                statusTextChanged |= statusTextChangedByKeepAlive;
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

        private void SetKeepAlive(
            SessionKeepAliveSnapshot snapshot,
            out bool statusTextChanged)
        {
            var previousStatus = SessionKeepAliveStatus;

            if (SetProperty(ref _sessionKeepAliveStatus, snapshot.Status, nameof(SessionKeepAliveStatus)))
            {
                OnPropertyChanged(nameof(IsDead));
                OnPropertyChanged(nameof(IsAliveOrUnknown));
                OnPropertyChanged(nameof(IsKeepAliveOther));
                OnPropertyChanged(nameof(ShowNormalStatusBubbles));
                OnPropertyChanged(nameof(ShowAttentionStatusBubble));
                OnPropertyChanged(nameof(CanOpenCli));
                OnPropertyChanged(nameof(CanRestart));
                OnPropertyChanged(nameof(ActivityText));
                OnPropertyChanged(nameof(StateTimestampText));
            }

            statusTextChanged = previousStatus != SessionKeepAliveStatus;
        }

        private string FormatActivity()
        {
            if (IsDead)
            {
                return "Dead";
            }

            if (IsKeepAliveOther)
            {
                return "CLI attention";
            }

            return IsActive switch
            {
                true => "Active",
                false => "Idle",
                _ => "Unknown",
            };
        }

        private string FormatStatus()
        {
            if (IsDead)
            {
                return "Dead";
            }

            if (IsKeepAliveOther)
            {
                return "CLI attention";
            }

            return LaunchState switch
            {
                SessionLaunchState.Requested => "Requested",
                SessionLaunchState.Launched => "Launched",
                SessionLaunchState.Started => FormatActivity(),
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

    public enum SessionLaunchState
    {
        Requested,
        Launched,
        Started,
    }
}
