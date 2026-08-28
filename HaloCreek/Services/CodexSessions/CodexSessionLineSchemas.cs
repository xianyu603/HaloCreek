using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace HaloCreek.Services.CodexSessions
{
    internal sealed record CodexSessionLineSchema<TLine>(
        string? RecordType,
        string? PayloadType = null,
        Func<TLine, bool>? IsMatch = null,
        Action<TLine>? Validate = null)
        where TLine : class;

    internal static class CodexSessionLineSchemas
    {
        public static readonly CodexSessionLineSchema<CodexTimestampedLine> Timestamped = new(
            RecordType: null,
            IsMatch: line => line.Timestamp is not null);

        public static readonly CodexSessionLineSchema<CodexSessionMetaLine> SessionMeta = new(
            RecordType: "session_meta",
            Validate: ValidateSessionMeta);

        public static readonly CodexSessionLineSchema<CodexUserMessageLine> UserMessage = new(
            RecordType: "event_msg",
            PayloadType: "user_message",
            IsMatch: line => !string.IsNullOrWhiteSpace(line.Payload.Message));

        public static readonly CodexSessionLineSchema<CodexAgentMessageLine> AgentMessage = new(
            RecordType: "event_msg",
            PayloadType: "agent_message",
            IsMatch: line => !string.IsNullOrWhiteSpace(line.Payload.Message));

        public static readonly CodexSessionLineSchema<CodexCompletedItemMessageLine> CompletedUserMessage = new(
            RecordType: "event_msg",
            PayloadType: "item_completed",
            IsMatch: line =>
                string.Equals(line.Payload.Item?.Type, "UserMessage", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(line.Payload.Item.Message));

        public static readonly CodexSessionLineSchema<CodexCompletedItemMessageLine> CompletedAgentMessage = new(
            RecordType: "event_msg",
            PayloadType: "item_completed",
            IsMatch: line =>
                string.Equals(line.Payload.Item?.Type, "AgentMessage", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(line.Payload.Item.Message));

        public static readonly CodexSessionLineSchema<CodexTaskStateLine> TaskStarted = new(
            RecordType: "event_msg",
            PayloadType: "task_started");

        public static readonly CodexSessionLineSchema<CodexTaskStateLine> TurnAborted = new(
            RecordType: "event_msg",
            PayloadType: "turn_aborted");

        public static readonly CodexSessionLineSchema<CodexTaskStateLine> TaskComplete = new(
            RecordType: "event_msg",
            PayloadType: "task_complete");

        public static readonly CodexSessionLineSchema<CodexTokenCountLine> TokenCount = new(
            RecordType: "event_msg",
            PayloadType: "token_count");

        private static void ValidateSessionMeta(CodexSessionMetaLine line)
        {
            ThrowIfEmpty(line.Payload.Id, "Session id is missing.");
            ThrowIfEmpty(line.Payload.Cwd, "Session workspace path is missing.");
        }

        private static void ThrowIfEmpty(string value, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(message);
            }
        }
    }

    internal sealed class CodexTimestampedLine
    {
        [JsonPropertyName("timestamp")]
        public DateTimeOffset? Timestamp { get; init; }
    }

    internal sealed class CodexSessionMetaLine
    {
        [JsonPropertyName("payload")]
        public required CodexSessionMetaPayload Payload { get; init; }
    }

    internal sealed class CodexSessionMetaPayload
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("cwd")]
        public required string Cwd { get; init; }

        [JsonPropertyName("timestamp")]
        public required DateTimeOffset Timestamp { get; init; }
    }

    internal sealed class CodexUserMessageLine
    {
        [JsonPropertyName("timestamp")]
        public required DateTimeOffset Timestamp { get; init; }

        [JsonPropertyName("payload")]
        public required CodexUserMessagePayload Payload { get; init; }
    }

    internal sealed class CodexUserMessagePayload
    {
        [JsonPropertyName("message")]
        public required string Message { get; init; }
    }

    internal sealed class CodexAgentMessageLine
    {
        [JsonPropertyName("timestamp")]
        public required DateTimeOffset Timestamp { get; init; }

        [JsonPropertyName("payload")]
        public required CodexAgentMessagePayload Payload { get; init; }
    }

    internal sealed class CodexAgentMessagePayload
    {
        [JsonPropertyName("message")]
        public required string Message { get; init; }

        [JsonPropertyName("phase")]
        public string? Phase { get; init; }
    }

    internal sealed class CodexCompletedItemMessageLine
    {
        [JsonPropertyName("timestamp")]
        public required DateTimeOffset Timestamp { get; init; }

        [JsonPropertyName("payload")]
        public required CodexCompletedItemMessagePayload Payload { get; init; }
    }

    internal sealed class CodexCompletedItemMessagePayload
    {
        [JsonPropertyName("item")]
        public CodexCompletedItemMessageItem? Item { get; init; }
    }

    internal sealed class CodexCompletedItemMessageItem
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("content")]
        public IReadOnlyList<CodexCompletedItemMessageContent>? Content { get; init; }

        [JsonPropertyName("phase")]
        public string? Phase { get; init; }

        public string Message => string.Join(
            "\n",
            Content?
                .Select(content => content.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
            ?? Array.Empty<string>());
    }

    internal sealed class CodexCompletedItemMessageContent
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }

    internal sealed class CodexTaskStateLine
    {
        [JsonPropertyName("timestamp")]
        public required DateTimeOffset Timestamp { get; init; }
    }

    internal sealed class CodexTokenCountLine
    {
        [JsonPropertyName("payload")]
        public required CodexTokenCountPayload Payload { get; init; }
    }

    internal sealed class CodexTokenCountPayload
    {
        [JsonPropertyName("info")]
        public CodexTokenCountInfo? Info { get; init; }
    }

    internal sealed class CodexTokenCountInfo
    {
        [JsonPropertyName("model_context_window")]
        public long? ModelContextWindow { get; init; }

        [JsonPropertyName("total_token_usage")]
        public CodexTokenUsage? TotalTokenUsage { get; init; }

        [JsonPropertyName("last_token_usage")]
        public CodexTokenUsage? LastTokenUsage { get; init; }
    }

    internal sealed class CodexTokenUsage
    {
        [JsonPropertyName("total_tokens")]
        public long? TotalTokens { get; init; }
    }
}
