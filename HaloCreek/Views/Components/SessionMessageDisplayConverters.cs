using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HaloCreek.Services.SessionState;

namespace HaloCreek.Views.Components
{
    public static class SessionMessageDisplayConverters
    {
        private static readonly IBrush UserAccent = new SolidColorBrush(Color.Parse("#0284C7"));
        private static readonly IBrush AgentAccent = new SolidColorBrush(Color.Parse("#16A34A"));
        private static readonly IBrush UnknownAccent = new SolidColorBrush(Color.Parse("#64748B"));
        private static readonly IBrush UserBackground = new SolidColorBrush(Color.Parse("#EFF6FF"));
        private static readonly IBrush AgentBackground = new SolidColorBrush(Color.Parse("#F0FDF4"));
        private static readonly IBrush UnknownBackground = new SolidColorBrush(Color.Parse("#F8FAFC"));
        private static readonly IBrush UserBorder = new SolidColorBrush(Color.Parse("#BFDBFE"));
        private static readonly IBrush AgentBorder = new SolidColorBrush(Color.Parse("#BBF7D0"));
        private static readonly IBrush UnknownBorder = new SolidColorBrush(Color.Parse("#CBD5E1"));

        public static IValueConverter BackgroundBrush { get; } = new SessionMessageBrushConverter(
            UserBackground,
            AgentBackground,
            UnknownBackground);

        public static IValueConverter BorderBrush { get; } = new SessionMessageBrushConverter(
            UserBorder,
            AgentBorder,
            UnknownBorder);

        public static IValueConverter AccentBrush { get; } = new SessionMessageBrushConverter(
            UserAccent,
            AgentAccent,
            UnknownAccent);

        public static IValueConverter SenderText { get; } = new SessionMessageSenderTextConverter();

        public static IValueConverter PhaseText { get; } = new SessionMessagePhaseTextConverter();

        public static IValueConverter TimestampText { get; } = new SessionMessageTimestampTextConverter();

        private sealed class SessionMessageBrushConverter : IValueConverter
        {
            private readonly IBrush _userBrush;
            private readonly IBrush _agentBrush;
            private readonly IBrush _unknownBrush;

            public SessionMessageBrushConverter(
                IBrush userBrush,
                IBrush agentBrush,
                IBrush unknownBrush)
            {
                _userBrush = userBrush;
                _agentBrush = agentBrush;
                _unknownBrush = unknownBrush;
            }

            public object Convert(
                object? value,
                Type targetType,
                object? parameter,
                CultureInfo culture)
            {
                return value switch
                {
                    SessionMessageSender.User => _userBrush,
                    SessionMessageSender.Agent => _agentBrush,
                    _ => _unknownBrush,
                };
            }

            public object ConvertBack(
                object? value,
                Type targetType,
                object? parameter,
                CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class SessionMessageSenderTextConverter : IValueConverter
        {
            public object Convert(
                object? value,
                Type targetType,
                object? parameter,
                CultureInfo culture)
            {
                return value switch
                {
                    SessionMessageSender.User => "User",
                    SessionMessageSender.Agent => "Agent",
                    _ => "Unknown",
                };
            }

            public object ConvertBack(
                object? value,
                Type targetType,
                object? parameter,
                CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class SessionMessagePhaseTextConverter : IValueConverter
        {
            public object Convert(
                object? value,
                Type targetType,
                object? parameter,
                CultureInfo culture)
            {
                return value switch
                {
                    SessionMessagePhase.Commentary => "commentary",
                    SessionMessagePhase.FinalAnswer => "final",
                    _ => "message",
                };
            }

            public object ConvertBack(
                object? value,
                Type targetType,
                object? parameter,
                CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class SessionMessageTimestampTextConverter : IValueConverter
        {
            public object Convert(
                object? value,
                Type targetType,
                object? parameter,
                CultureInfo culture)
            {
                return value is DateTimeOffset timestamp
                    ? timestamp.ToLocalTime().ToString("HH:mm:ss", culture)
                    : string.Empty;
            }

            public object ConvertBack(
                object? value,
                Type targetType,
                object? parameter,
                CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
