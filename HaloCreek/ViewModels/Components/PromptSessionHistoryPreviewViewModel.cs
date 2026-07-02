using System;
using System.Collections.Generic;
using Avalonia.Media;
using HaloCreek.Services.SessionState;

namespace HaloCreek.ViewModels.Components
{
    public sealed class PromptSessionHistoryPreviewViewModel
    {
        private PromptSessionHistoryPreviewViewModel(
            string title,
            SessionStateSnapshot snapshot,
            IReadOnlyList<PromptSessionHistoryMessageViewModel> messages)
        {
            Title = title;
            StateText = FormatState(snapshot.State);
            StateTimestampText = snapshot.StateTimestamp is null
                ? "No state timestamp"
                : $"Updated {snapshot.StateTimestamp.Value.ToLocalTime():HH:mm:ss}";
            TokenSummaryText = FormatTokenSummary(snapshot.TokenInfo);
            Messages = messages;
        }

        public string Title { get; }

        public string StateText { get; }

        public string StateTimestampText { get; }

        public string TokenSummaryText { get; }

        public IReadOnlyList<PromptSessionHistoryMessageViewModel> Messages { get; }

        public static PromptSessionHistoryPreviewViewModel CreateDummy()
        {
            var now = DateTimeOffset.Now;
            var messages = new[]
            {
                new SessionMessage(
                    now.AddMinutes(-8),
                    SessionMessageSender.User,
                    "把 PromptInputView 右侧加一个 ongoing session 历史预览，先只要假数据，我看样式。",
                    SessionMessagePhase.Unknown),
                new SessionMessage(
                    now.AddMinutes(-7),
                    SessionMessageSender.Agent,
                    "我会先保持输入区行为不变，再把会话状态按 SessionStateSnapshot 的形状映射到一个轻量 VM。",
                    SessionMessagePhase.Commentary),
                new SessionMessage(
                    now.AddMinutes(-4),
                    SessionMessageSender.User,
                    "右侧需要能看出 user/agent、phase、时间和主要内容，后面再接真实数据。",
                    SessionMessagePhase.Unknown),
                new SessionMessage(
                    now.AddMinutes(-1),
                    SessionMessageSender.Agent,
                    "UI 壳子已准备好：顶部展示前台 session 状态，中间滚动消息，底部保留 token 摘要。",
                    SessionMessagePhase.FinalAnswer),
            };
            var snapshot = new SessionStateSnapshot(
                SessionTaskState.TaskStarted,
                now.AddSeconds(-22),
                messages,
                new SessionTokenInfo(200_000, 42_760, 1_438));

            return new PromptSessionHistoryPreviewViewModel(
                "Frontend session preview",
                snapshot,
                Array.ConvertAll(messages, message => new PromptSessionHistoryMessageViewModel(message)));
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

        private static string FormatTokenSummary(SessionTokenInfo? tokenInfo)
        {
            // TODO 这里需要改成precentage total XX, context XX% usaged(拿last/context算)
            if (tokenInfo is null)
            {
                return "Token usage unavailable";
            }

            var context = tokenInfo.ContextWindow is null
                ? "context ?"
                : $"context {tokenInfo.ContextWindow.Value:N0}";
            var total = tokenInfo.TotalTokenUsageTotal is null
                ? "total ?"
                : $"total {tokenInfo.TotalTokenUsageTotal.Value:N0}";
            var last = tokenInfo.LastTokenUsageTotal is null
                ? "last ?"
                : $"last {tokenInfo.LastTokenUsageTotal.Value:N0}";

            return $"{total} / {context} · {last}";
        }
    }

    public sealed class PromptSessionHistoryMessageViewModel
    {
        private static readonly IBrush UserAccent = new SolidColorBrush(Color.Parse("#0284C7"));
        private static readonly IBrush AgentAccent = new SolidColorBrush(Color.Parse("#16A34A"));
        private static readonly IBrush UserBackground = new SolidColorBrush(Color.Parse("#EFF6FF"));
        private static readonly IBrush AgentBackground = new SolidColorBrush(Color.Parse("#F0FDF4"));
        private static readonly IBrush UserBorder = new SolidColorBrush(Color.Parse("#BFDBFE"));
        private static readonly IBrush AgentBorder = new SolidColorBrush(Color.Parse("#BBF7D0"));

        public PromptSessionHistoryMessageViewModel(SessionMessage message)
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
