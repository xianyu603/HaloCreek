using System;
using System.Collections.Generic;
using System.IO;
using HaloCreek.Infrastructure;
using HaloCreek.Services.CodexSessions;

namespace HaloCreek.Services.SessionState
{
    public static class CodexSessionStateReader
    {
        public static SessionStateSnapshot ReadSessionState(
            string sessionId,
            object? readHint)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            if (readHint is string hintedSessionFilePath
                && !string.IsNullOrWhiteSpace(hintedSessionFilePath)
                && TryReadSessionFile(hintedSessionFilePath, out var hintedSnapshot))
            {
                return hintedSnapshot;
            }

            var sessionFilePath = CodexSessionFileLocator.FindSessionFilePath(sessionId);
            if (sessionFilePath is null)
            {
                return SessionStateSnapshot.CreateEmpty();
            }

            return ReadSessionFile(sessionFilePath);
        }

        private static bool TryReadSessionFile(
            string sessionFilePath,
            out SessionStateSnapshot snapshot)
        {
            try
            {
                snapshot = ReadSessionFile(sessionFilePath);
                return true;
            }
            catch (Exception ex) when (ex is IOException
                or NotSupportedException
                or UnauthorizedAccessException)
            {
                snapshot = default!;
                return false;
            }
        }

        private static SessionStateSnapshot ReadSessionFile(string sessionFilePath)
        {
            if (!File.Exists(sessionFilePath))
            {
                throw new FileNotFoundException(
                    "Codex session file does not exist.",
                    sessionFilePath);
            }

            var lastActiveTime = new DateTimeOffset(File.GetLastWriteTimeUtc(sessionFilePath));
            var state = SessionTaskState.Unknown;
            DateTimeOffset? stateTimestamp = null;
            var messages = new List<SessionMessage>();
            SessionTokenInfo? tokenInfo = null;

            foreach (var line in CodexSessionJsonlReader.ReadLines(sessionFilePath))
            {
                if (line.TryRead(CodexSessionLineSchemas.UserMessage, out var userMessage))
                {
                    messages.Add(new SessionMessage(
                        userMessage.Timestamp,
                        SessionMessageSender.User,
                        userMessage.Payload.Message,
                        SessionMessagePhase.Unknown));
                    continue;
                }

                if (line.TryRead(CodexSessionLineSchemas.AgentMessage, out var agentMessage))
                {
                    messages.Add(new SessionMessage(
                        agentMessage.Timestamp,
                        SessionMessageSender.Agent,
                        agentMessage.Payload.Message,
                        ReadMessagePhase(agentMessage.Payload.Phase)));
                    continue;
                }

                if (line.TryRead(CodexSessionLineSchemas.TaskStarted, out var taskStarted))
                {
                    state = SessionTaskState.TaskStarted;
                    stateTimestamp = taskStarted.Timestamp;
                    continue;
                }

                if (line.TryRead(CodexSessionLineSchemas.TurnAborted, out var turnAborted))
                {
                    state = SessionTaskState.TaskAborted;
                    stateTimestamp = turnAborted.Timestamp;
                    continue;
                }

                if (line.TryRead(CodexSessionLineSchemas.TaskComplete, out var taskComplete))
                {
                    state = SessionTaskState.TaskCompleted;
                    stateTimestamp = taskComplete.Timestamp;
                    continue;
                }

                if (line.TryRead(CodexSessionLineSchemas.TokenCount, out var tokenCount))
                {
                    tokenInfo = ReadTokenInfo(tokenCount);
                }
            }

            return new SessionStateSnapshot(
                state,
                stateTimestamp,
                lastActiveTime,
                CalculateActive(lastActiveTime),
                messages,
                tokenInfo) with
            {
                SnapshotListenPath = sessionFilePath,
            };
        }

        private static bool? CalculateActive(DateTimeOffset? lastActiveTime)
        {
            if (lastActiveTime is null)
            {
                return null;
            }

            return DateTimeOffset.UtcNow - lastActiveTime.Value.ToUniversalTime()
                < SessionStateSnapshot.ActiveThreshold;
        }

        private static SessionMessagePhase ReadMessagePhase(string? phase)
        {
            if (phase is null)
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

        private static SessionTokenInfo ReadTokenInfo(CodexTokenCountLine line)
        {
            var info = line.Payload.Info;
            if (info is null)
            {
                return new SessionTokenInfo(null, null, null);
            }

            return new SessionTokenInfo(
                info.ModelContextWindow,
                info.TotalTokenUsage?.TotalTokens,
                info.LastTokenUsage?.TotalTokens);
        }

    }
}
