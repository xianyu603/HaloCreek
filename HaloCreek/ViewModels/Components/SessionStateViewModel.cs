using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
using HaloCreek.Models;
using HaloCreek.Services.SessionState;

namespace HaloCreek.ViewModels.Components
{
    public sealed class SessionStateViewModel : ViewModelBase
    {
        private string _title = string.Empty;
        private string _activityText = string.Empty;
        private string _stateText = string.Empty;
        private string _stateTimestampText = string.Empty;
        private string _tokenSummaryText = string.Empty;

        private SessionStateViewModel()
        {
        }

        public string Title => _title;

        public string ActivityText => _activityText;

        public string StateText => _stateText;

        public string StateTimestampText => _stateTimestampText;

        public string TokenSummaryText => _tokenSummaryText;

        public ObservableCollection<SessionStateMessageViewModel> Messages { get; } = [];

        public static SessionStateViewModel CreateEmpty()
        {
            var viewModel = new SessionStateViewModel();
            viewModel.UpdateEmpty();
            return viewModel;
        }

        public void UpdateEmpty()
        {
            Update(
                "No front session",
                "No session",
                SessionStateSnapshot.CreateEmpty(),
                Array.Empty<SessionMessage>());
        }

        public void UpdateFromSnapshot(
            OngoingSessionInfo session,
            SessionStateSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(snapshot);

            Update(
                string.IsNullOrWhiteSpace(session.Title) ? "Front session" : session.Title,
                FormatActivity(snapshot.Active),
                snapshot,
                snapshot.Messages);
        }

        private void Update(
            string title,
            string activityText,
            SessionStateSnapshot snapshot,
            IReadOnlyList<SessionMessage> messages)
        {
            SetProperty(ref _title, title, nameof(Title));
            SetProperty(ref _activityText, activityText, nameof(ActivityText));
            SetProperty(ref _stateText, FormatState(snapshot.State), nameof(StateText));
            SetProperty(
                ref _stateTimestampText,
                snapshot.StateTimestamp is null
                    ? "No state timestamp"
                    : $"Updated {snapshot.StateTimestamp.Value.ToLocalTime():HH:mm:ss}",
                nameof(StateTimestampText));
            SetProperty(ref _tokenSummaryText, FormatTokenSummary(snapshot.TokenInfo), nameof(TokenSummaryText));
            UpdateMessages(messages);
        }

        private void UpdateMessages(IReadOnlyList<SessionMessage> messages)
        {
            var commonCount = Math.Min(Messages.Count, messages.Count);
            for (var index = 0; index < commonCount; index++)
            {
                if (!Equals(Messages[index].Source, messages[index]))
                {
                    ReplaceMessages(messages);
                    return;
                }
            }

            while (Messages.Count > messages.Count)
            {
                Messages.RemoveAt(Messages.Count - 1);
            }

            for (var index = Messages.Count; index < messages.Count; index++)
            {
                Messages.Add(new SessionStateMessageViewModel(messages[index]));
            }
        }

        private void ReplaceMessages(IReadOnlyList<SessionMessage> messages)
        {
            Messages.Clear();
            for (var index = 0; index < messages.Count; index++)
            {
                Messages.Add(new SessionStateMessageViewModel(messages[index]));
            }
        }

        private static string FormatState(SessionTaskState state)
        {
            return state switch
            {
                SessionTaskState.TaskStarted => "Started",
                SessionTaskState.TaskAborted => "Aborted",
                SessionTaskState.TaskCompleted => "Completed",
                _ => "Unknown",
            };
        }

        private static string FormatActivity(bool? active)
        {
            return active switch
            {
                true => "Active",
                false => "Idle",
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
            var usedPercentage = tokenInfo.ContextWindow is null || tokenInfo.LastTokenUsageTotal is null ? "?" : 
                $"{100 * tokenInfo.LastTokenUsageTotal.Value / tokenInfo.ContextWindow.Value:N0}";

            return $"context {usedPercentage}% used, {total}";
        }
    }

    public sealed class SessionStateMessageViewModel
    {
        private static readonly IBrush UserAccent = new SolidColorBrush(Color.Parse("#0284C7"));
        private static readonly IBrush AgentAccent = new SolidColorBrush(Color.Parse("#16A34A"));
        private static readonly IBrush UserBackground = new SolidColorBrush(Color.Parse("#EFF6FF"));
        private static readonly IBrush AgentBackground = new SolidColorBrush(Color.Parse("#F0FDF4"));
        private static readonly IBrush UserBorder = new SolidColorBrush(Color.Parse("#BFDBFE"));
        private static readonly IBrush AgentBorder = new SolidColorBrush(Color.Parse("#BBF7D0"));

        public SessionStateMessageViewModel(SessionMessage message)
        {
            Source = message;
            SenderText = message.Sender switch
            {
                SessionMessageSender.User => "User",
                SessionMessageSender.Agent => "Agent",
                _ => "Unknown",
            };
            PhaseText = message.Phase switch
            {
                SessionMessagePhase.Commentary => "commentary",
                SessionMessagePhase.FinalAnswer => "final",
                _ => "message",
            };
            TimestampText = message.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            Message = message.Message;
            AccentBrush = message.Sender == SessionMessageSender.User ? UserAccent : AgentAccent;
            BackgroundBrush = message.Sender == SessionMessageSender.User ? UserBackground : AgentBackground;
            BorderBrush = message.Sender == SessionMessageSender.User ? UserBorder : AgentBorder;
        }

        internal SessionMessage Source { get; }

        public string SenderText { get; }

        public string PhaseText { get; }

        public string TimestampText { get; }

        public string Message { get; }

        public IBrush AccentBrush { get; }

        public IBrush BackgroundBrush { get; }

        public IBrush BorderBrush { get; }
    }
}
