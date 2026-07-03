using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HaloCreek.Infrastructure;

namespace HaloCreek.Services.SessionState
{
    public static class CodexSessionStateReader
    {
        public static SessionStateSnapshot ReadSessionState(string sessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            var sessionFilePath = CodexSessionFileLocator.FindSessionFilePath(sessionId);
            if (sessionFilePath is null)
            {
                return SessionStateSnapshot.CreateEmpty();
            }

            return ReadSessionFile(sessionFilePath);
        }

        private static SessionStateSnapshot ReadSessionFile(string sessionFilePath)
        {
            var state = SessionTaskState.Unknown;
            DateTimeOffset? stateTimestamp = null;
            var messages = new List<SessionMessage>();
            SessionTokenInfo? tokenInfo = null;

            foreach (var line in File.ReadLines(sessionFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                if (!IsRecordType(root, "event_msg"))
                {
                    continue;
                }

                var payload = GetRequiredObject(root, "payload");
                var payloadType = GetRequiredString(payload, "type");

                if (TryReadMessage(root, payload, payloadType, out var message))
                {
                    messages.Add(message);
                    continue;
                }

                if (TryReadTaskState(root, payloadType, out var taskState, out var taskStateTimestamp))
                {
                    state = taskState;
                    stateTimestamp = taskStateTimestamp;
                    continue;
                }

                if (string.Equals(payloadType, "token_count", StringComparison.Ordinal))
                {
                    tokenInfo = ReadTokenInfo(payload);
                }
            }

            return new SessionStateSnapshot(
                state,
                stateTimestamp,
                messages,
                tokenInfo)
            {
                // WSL file watching is temporarily disabled. Timer polling still refreshes session state.
                // SnapshotListenPath = sessionFilePath,
            };
        }

        private static bool TryReadMessage(
            JsonElement root,
            JsonElement payload,
            string payloadType,
            out SessionMessage message)
        {
            message = default!;

            if (string.Equals(payloadType, "user_message", StringComparison.Ordinal))
            {
                if (!TryGetNonEmptyString(payload, "message", out var userMessage))
                {
                    return false;
                }

                message = new SessionMessage(
                    GetRequiredTimestamp(root),
                    SessionMessageSender.User,
                    userMessage,
                    SessionMessagePhase.Unknown);
                return true;
            }

            if (string.Equals(payloadType, "agent_message", StringComparison.Ordinal))
            {
                if (!TryGetNonEmptyString(payload, "message", out var agentMessage))
                {
                    return false;
                }

                message = new SessionMessage(
                    GetRequiredTimestamp(root),
                    SessionMessageSender.Agent,
                    agentMessage,
                    ReadMessagePhase(payload));
                return true;
            }

            return false;
        }

        private static bool TryReadTaskState(
            JsonElement root,
            string payloadType,
            out SessionTaskState state,
            out DateTimeOffset timestamp)
        {
            state = SessionTaskState.Unknown;
            timestamp = default;

            if (string.Equals(payloadType, "task_started", StringComparison.Ordinal))
            {
                state = SessionTaskState.TaskStarted;
            }
            else if (string.Equals(payloadType, "turn_aborted", StringComparison.Ordinal))
            {
                state = SessionTaskState.TaskAborted;
            }
            else if (string.Equals(payloadType, "task_complete", StringComparison.Ordinal))
            {
                state = SessionTaskState.TaskCompleted;
            }
            else
            {
                return false;
            }

            timestamp = GetRequiredTimestamp(root);
            return true;
        }

        private static SessionMessagePhase ReadMessagePhase(JsonElement payload)
        {
            if (!TryGetString(payload, "phase", out var phase))
            {
                return SessionMessagePhase.Unknown;
            }

            return phase switch
            {
                "commentary" => SessionMessagePhase.Commentary,
                "final_answer" => SessionMessagePhase.FinalAnswer,
                _ => SessionMessagePhase.Unknown,
            };
        }

        private static SessionTokenInfo ReadTokenInfo(JsonElement payload)
        {
            if (!TryGetOptionalObject(payload, "info", out var info))
            {
                return new SessionTokenInfo(null, null, null);
            }

            return new SessionTokenInfo(
                ReadContextWindow(info),
                ReadNestedTotal(info, "total_token_usage"),
                ReadNestedTotal(info, "last_token_usage"));
        }

        private static long? ReadContextWindow(JsonElement info)
        {
            if (TryGetInt64(info, "model_context_window", out var modelContextWindow))
            {
                return modelContextWindow;
            }

            return null;
        }

        private static long? ReadNestedTotal(JsonElement info, string propertyName)
        {
            if (!TryGetOptionalObject(info, propertyName, out var usage))
            {
                return null;
            }

            return TryGetInt64(usage, "total_tokens", out var total)
                ? total
                : null;
        }

        private static bool IsRecordType(JsonElement root, string expectedType)
        {
            return root.TryGetProperty("type", out var typeElement)
                && typeElement.ValueKind == JsonValueKind.String
                && string.Equals(typeElement.GetString(), expectedType, StringComparison.Ordinal);
        }

        private static JsonElement GetRequiredObject(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{propertyName} is missing.");
            }

            return property;
        }

        private static bool TryGetOptionalObject(
            JsonElement element,
            string propertyName,
            out JsonElement property)
        {
            if (!element.TryGetProperty(propertyName, out property))
            {
                property = default;
                return false;
            }

            if (property.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{propertyName} must be an object.");
            }

            return true;
        }

        private static string GetRequiredString(JsonElement element, string propertyName)
        {
            if (!TryGetString(element, propertyName, out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"{propertyName} is missing.");
            }

            return value;
        }

        private static bool TryGetNonEmptyString(
            JsonElement element,
            string propertyName,
            out string value)
        {
            if (TryGetString(element, propertyName, out value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static bool TryGetString(
            JsonElement element,
            string propertyName,
            out string value)
        {
            value = string.Empty;

            if (!element.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = property.GetString() ?? string.Empty;
            return true;
        }

        private static DateTimeOffset GetRequiredTimestamp(JsonElement element)
        {
            if (!TryGetString(element, "timestamp", out var value)
                || !DateTimeOffset.TryParse(value, out var timestamp))
            {
                throw new InvalidDataException("timestamp is missing.");
            }

            return timestamp;
        }

        private static bool TryGetInt64(
            JsonElement element,
            string propertyName,
            out long value)
        {
            value = default;

            return element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt64(out value);
        }
    }
}
