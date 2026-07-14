using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HaloCreek.Infrastructure;
using HaloCreek.Models;
using HaloCreek.Services.CodexSessions;

namespace HaloCreek.Services.SessionHistory
{
    public static class CodexSessionHistoryReader
    {
        private const string JsonlSearchPattern = "*.jsonl";
        private static readonly EnumerationOptions SessionFileEnumerationOptions = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
        };

        private static readonly Dictionary<string, CachedSessionFile> _fileCache = new(GetFilePathComparer());

        public static SessionHistoryResult ReadSessions(string workspacePath, int maxSessionHistoryFiles)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            if (maxSessionHistoryFiles <= 0)
            {
                return new SessionHistoryResult(Array.Empty<HistorySessionInfo>(), 0, null);
            }

            var sessionHistoryRootPath = CodexSessionFileLocator.FindSessionRootPath();
            if (sessionHistoryRootPath is null)
            {
                return new SessionHistoryResult(Array.Empty<HistorySessionInfo>(), 0, null);
            }

            var sessionFiles = EnumerateLatestSessionFiles(
                sessionHistoryRootPath,
                maxSessionHistoryFiles);

            var sessions = new List<HistorySessionInfo>();
            var skippedFileCount = 0;

            foreach (var sessionFile in sessionFiles)
            {
                HistorySessionInfo session;
                try
                {
                    session = ReadSessionFile(sessionFile.FilePath);
                }
                catch (Exception ex) when (ex is JsonException
                    or IOException
                    or InvalidDataException
                    or NotSupportedException
                    or UnauthorizedAccessException)
                {
                    skippedFileCount++;
                    continue;
                }

                if (PlatformInfrastructure.AreWorkspacePathsEquivalent(
                    session.WorkspacePath,
                    workspacePath))
                {
                    sessions.Add(session);
                }
            }

            return new SessionHistoryResult(sessions, skippedFileCount, sessionHistoryRootPath);
        }

        private static IReadOnlyList<SessionFileMetadata> EnumerateLatestSessionFiles(
            string sessionHistoryRootPath,
            int maxSessionHistoryFiles)
        {
            try
            {
                return Directory
                    .EnumerateFiles(
                        sessionHistoryRootPath,
                        JsonlSearchPattern,
                        SessionFileEnumerationOptions)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Take(maxSessionHistoryFiles)
                    .Select(file => new SessionFileMetadata(
                        file.FullName,
                        file.LastWriteTimeUtc,
                        file.Length))
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException
                or NotSupportedException
                or UnauthorizedAccessException)
            {
                return Array.Empty<SessionFileMetadata>();
            }
        }

        private static CachedSessionFile GetCachedSessionFile(SessionFileMetadata sessionFile)
        {
            if (_fileCache.TryGetValue(sessionFile.FilePath, out var cachedSessionFile)
                && cachedSessionFile.Metadata.HasSameContentAs(sessionFile))
            {
                return cachedSessionFile;
            }

            try
            {
                cachedSessionFile = new CachedSessionFile(
                    sessionFile,
                    ReadSessionFile(sessionFile.FilePath),
                    WasParsedSuccessfully: true);
            }
            catch (Exception ex) when (ex is JsonException
                or IOException
                or InvalidDataException
                or NotSupportedException
                or UnauthorizedAccessException)
            {
                cachedSessionFile = new CachedSessionFile(
                    sessionFile,
                    Session: null,
                    WasParsedSuccessfully: false);
            }

            // TODO: 后续如果恢复增量缓存，应由 Store 提供 scan context 或明确缓存所有权。
            // _fileCache[sessionFile.FilePath] = cachedSessionFile;
            return cachedSessionFile;
        }

        private static void PruneFileCache(IReadOnlyList<SessionFileMetadata> sessionFiles)
        {
            // 删掉之前缓存 但是本轮没有枚举的cache
            var activePaths = new HashSet<string>(
                sessionFiles.Select(sessionFile => sessionFile.FilePath),
                _fileCache.Comparer);
            var cachedPaths = _fileCache.Keys.ToArray();

            foreach (var cachedPath in cachedPaths)
            {
                if (!activePaths.Contains(cachedPath))
                {
                    _fileCache.Remove(cachedPath);
                }
            }
        }

        private static HistorySessionInfo ReadSessionFile(string sessionFilePath)
        {
            string? id = null;
            string? sessionWorkspacePath = null;
            DateTimeOffset? createdAt = null;
            DateTimeOffset? lastUpdatedAt = null;
            string? initialPrompt = null;
            var lastPrompt = string.Empty;
            var lastReply = string.Empty;
            var foundSessionMeta = false;

            foreach (var line in CodexSessionJsonlReader.ReadLines(sessionFilePath))
            {
                if (line.TryRead(CodexSessionLineSchemas.Timestamped, out var timestamped))
                {
                    lastUpdatedAt = timestamped.Timestamp!.Value;
                }

                if (line.TryRead(CodexSessionLineSchemas.UserMessage, out var userMessage)
                    && !IsEnvironmentContextMessage(userMessage.Payload.Message))
                {
                    // If this source renders poorly, switch to response_item role=user instead.
                    initialPrompt ??= userMessage.Payload.Message;
                    lastPrompt = userMessage.Payload.Message;
                }

                if (line.TryRead(CodexSessionLineSchemas.AgentMessage, out var agentMessage))
                {
                    lastReply = agentMessage.Payload.Message;
                }

                if (!line.TryRead(CodexSessionLineSchemas.SessionMeta, out var sessionMeta))
                {
                    continue;
                }

                foundSessionMeta = true;
                id = sessionMeta.Payload.Id;
                sessionWorkspacePath = sessionMeta.Payload.Cwd;
                createdAt = sessionMeta.Payload.Timestamp;
            }

            if (!foundSessionMeta) throw new InvalidDataException("Session metadata is missing.");
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("Session id is missing.");
            if (string.IsNullOrWhiteSpace(sessionWorkspacePath)) throw new InvalidDataException("Session workspace path is missing.");
            if (createdAt is null) throw new InvalidDataException("Session created timestamp is missing.");
            if (lastUpdatedAt is null) throw new InvalidDataException("Session last updated timestamp is missing.");

            return new HistorySessionInfo(
                id,
                sessionWorkspacePath,
                createdAt.Value,
                lastUpdatedAt.Value,
                initialPrompt ?? string.Empty,
                lastPrompt,
                lastReply,
                sessionFilePath);
        }

        private static bool IsEnvironmentContextMessage(string message)
        {
            var trimmedMessage = message.TrimStart();
            return trimmedMessage.StartsWith("<environment_context>", StringComparison.Ordinal)
                && trimmedMessage.Contains("</environment_context>", StringComparison.Ordinal);
        }

        private static StringComparer GetFilePathComparer()
        {
            return OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }

        private sealed record SessionFileMetadata(
            string FilePath,
            DateTime LastWriteTimeUtc,
            long Length)
        {
            public bool HasSameContentAs(SessionFileMetadata other)
            {
                return LastWriteTimeUtc == other.LastWriteTimeUtc
                    && Length == other.Length;
            }
        }

        private sealed record CachedSessionFile(
            SessionFileMetadata Metadata,
            HistorySessionInfo? Session,
            bool WasParsedSuccessfully);
    }
}
