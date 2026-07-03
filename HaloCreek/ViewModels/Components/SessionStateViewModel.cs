using System;
using System.Collections.Generic;
using Avalonia.Media;
using HaloCreek.Models;
using HaloCreek.Services.SessionState;

namespace HaloCreek.ViewModels.Components
{
    public sealed class SessionStateViewModel
    {
        private SessionStateViewModel(
            string title,
            string activityText,
            SessionStateSnapshot snapshot,
            IReadOnlyList<SessionStateMessageViewModel> messages)
        {
            Title = title;
            ActivityText = activityText;
            StateText = FormatState(snapshot.State);
            StateTimestampText = snapshot.StateTimestamp is null
                ? "No state timestamp"
                : $"Updated {snapshot.StateTimestamp.Value.ToLocalTime():HH:mm:ss}";
            TokenSummaryText = FormatTokenSummary(snapshot.TokenInfo);
            Messages = messages;
        }

        public string Title { get; }

        public string ActivityText { get; }

        public string StateText { get; }

        public string StateTimestampText { get; }

        public string TokenSummaryText { get; }

        public IReadOnlyList<SessionStateMessageViewModel> Messages { get; }

        public static SessionStateViewModel CreateEmpty()
        {
            return new SessionStateViewModel(
                "No front session",
                "No session",
                SessionStateSnapshot.CreateEmpty(),
                Array.Empty<SessionStateMessageViewModel>());
        }

        public static SessionStateViewModel FromSnapshot(
            OngoingSessionInfo session,
            SessionStateSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(snapshot);

            return new SessionStateViewModel(
                string.IsNullOrWhiteSpace(session.Title) ? "Front session" : session.Title,
                FormatActivity(session),
                snapshot,
                ConvertMessages(snapshot.Messages));
        }

        private static IReadOnlyList<SessionStateMessageViewModel> ConvertMessages(
            IReadOnlyList<SessionMessage> messages)
        {
            var convertedMessages = new SessionStateMessageViewModel[messages.Count];
            for (var index = 0; index < messages.Count; index++)
            {
                convertedMessages[index] = new SessionStateMessageViewModel(messages[index]);
            }

            return convertedMessages;
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

        private static string FormatActivity(OngoingSessionInfo session)
        {
            return session.HeartbeatState switch
            {
                TmuxHeartbeatState.Active => "Active",
                TmuxHeartbeatState.Idle => "Idle",
                _ => "Unknown",
            };
        }

        private static string FormatTokenSummary(SessionTokenInfo? tokenInfo)
        {
            // TODO 这里需要改成precentage total XX, context XX% usaged(拿last/context算)
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

        public string SenderText { get; }

        public string PhaseText { get; }

        public string TimestampText { get; }

        public string Message { get; }

        public IBrush AccentBrush { get; }

        public IBrush BackgroundBrush { get; }

        public IBrush BorderBrush { get; }
    }
}
