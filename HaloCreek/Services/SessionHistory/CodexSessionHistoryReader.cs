using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HaloCreek.Infrastructure;
using HaloCreek.Models;

namespace HaloCreek.Services.SessionHistory
{
    public sealed class CodexSessionHistoryReader : ISessionHistoryReader
    {
        private const string JsonlSearchPattern = "*.jsonl";
        private const string SessionsDirectoryName = "sessions";
        private const string CodexDirectoryName = ".codex";
        private static readonly EnumerationOptions SessionFileEnumerationOptions = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
        };

        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly Dictionary<string, CachedSessionFile> _fileCache = new(GetFilePathComparer());

        public CodexSessionHistoryReader(AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _platformInfrastructure = appCommonRuntime.PlatformInfrastructure;
        }

        public SessionHistoryResult ReadSessions(string workspacePath, int maxSessionHistoryFiles)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            if (maxSessionHistoryFiles <= 0)
            {
                return new SessionHistoryResult(Array.Empty<HistorySessionInfo>(), 0);
            }

            var sessionHistoryRootPath = FindSessionHistoryRootPath();
            if (sessionHistoryRootPath is null)
            {
                return new SessionHistoryResult(Array.Empty<HistorySessionInfo>(), 0);
            }

            var sessionFiles = EnumerateLatestSessionFiles(
                sessionHistoryRootPath,
                maxSessionHistoryFiles);

            var sessions = new List<HistorySessionInfo>();
            var skippedFileCount = 0;
            PruneFileCache(sessionFiles);

            foreach (var sessionFile in sessionFiles)
            {
                var cachedSessionFile = GetCachedSessionFile(sessionFile);
                if (!cachedSessionFile.WasParsedSuccessfully)
                {
                    skippedFileCount++;
                    continue;
                }

                if (cachedSessionFile.Session is not null
                    && PlatformInfrastructure.AreWorkspacePathsEquivalent(
                        cachedSessionFile.Session.WorkspacePath,
                        workspacePath))
                {
                    sessions.Add(cachedSessionFile.Session);
                }
            }

            return new SessionHistoryResult(sessions, skippedFileCount);
        }

        private string? FindSessionHistoryRootPath()
        {
            foreach (var candidatePath in GetSessionHistoryRootPathCandidates())
            {
                if (Directory.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return null;
        }

        private IEnumerable<string> GetSessionHistoryRootPathCandidates()
        {
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidatePath in GetCodexHomeSessionPathCandidates())
            {
                if (seenPaths.Add(candidatePath))
                {
                    yield return candidatePath;
                }
            }

            foreach (var homePath in GetHomePathCandidates())
            {
                var sessionHistoryPath = AppendPathSegments(
                    homePath,
                    CodexDirectoryName,
                    SessionsDirectoryName);
                foreach (var candidatePath in ExpandReadablePathCandidates(sessionHistoryPath))
                {
                    if (seenPaths.Add(candidatePath))
                    {
                        yield return candidatePath;
                    }
                }
            }
        }

        private IEnumerable<string> GetCodexHomeSessionPathCandidates()
        {
            var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(codexHome))
            {
                foreach (var candidatePath in ExpandReadablePathCandidates(
                    AppendPathSegments(codexHome, SessionsDirectoryName)))
                {
                    yield return candidatePath;
                }
            }

            if (_platformInfrastructure.TryGetWslEnvironmentVariable("CODEX_HOME", out var wslCodexHome))
            {
                foreach (var candidatePath in ExpandReadablePathCandidates(
                    AppendPathSegments(wslCodexHome, SessionsDirectoryName)))
                {
                    yield return candidatePath;
                }
            }
        }

        private IEnumerable<string> GetHomePathCandidates()
        {
            var homeEnvironmentPath = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(homeEnvironmentPath))
            {
                yield return homeEnvironmentPath;
            }

            if (_platformInfrastructure.TryGetWslEnvironmentVariable("HOME", out var wslHomePath))
            {
                yield return wslHomePath;
            }

            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfilePath))
            {
                yield return userProfilePath;
            }
        }

        private IEnumerable<string> ExpandReadablePathCandidates(string path)
        {
            yield return path;

            foreach (var candidatePath in _platformInfrastructure.GetReadableWslPathCandidates(path))
            {
                yield return candidatePath;
            }
        }

        private static string AppendPathSegments(string path, params string[] segments)
        {
            var separator = path.Contains('/')
                && !path.Contains('\\')
                    ? '/'
                    : Path.DirectorySeparatorChar;

            return path.TrimEnd('/', '\\')
                + separator
                + string.Join(separator.ToString(), segments);
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

        private CachedSessionFile GetCachedSessionFile(SessionFileMetadata sessionFile)
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

            _fileCache[sessionFile.FilePath] = cachedSessionFile;
            return cachedSessionFile;
        }

        private void PruneFileCache(IReadOnlyList<SessionFileMetadata> sessionFiles)
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

            foreach (var line in File.ReadLines(sessionFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;

                if (TryGetTimestamp(root, out var recordTimestamp))
                {
                    lastUpdatedAt = recordTimestamp;
                }

                if (TryReadEventUserMessage(root, out var eventUserPrompt))
                {
                    // If this source renders poorly, switch to response_item role=user instead.
                    initialPrompt ??= eventUserPrompt;
                    lastPrompt = eventUserPrompt;
                }

                if (TryReadAssistantReply(root, out var assistantReply))
                {
                    lastReply = assistantReply;
                }

                if (!IsRecordType(root, "session_meta"))
                {
                    continue;
                }

                foundSessionMeta = true;
                var payload = GetRequiredObject(root, "payload");

                id = GetRequiredString(payload, "id");
                sessionWorkspacePath = GetRequiredString(payload, "cwd");
                createdAt = GetRequiredTimestamp(payload, "timestamp");
            }

            if (!foundSessionMeta)
            {
                throw new InvalidDataException("Session metadata is missing.");
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException("Session id is missing.");
            }

            if (string.IsNullOrWhiteSpace(sessionWorkspacePath))
            {
                throw new InvalidDataException("Session workspace path is missing.");
            }

            if (createdAt is null)
            {
                throw new InvalidDataException("Session created timestamp is missing.");
            }

            if (lastUpdatedAt is null)
            {
                throw new InvalidDataException("Session last updated timestamp is missing.");
            }

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

        private static bool TryReadEventUserMessage(JsonElement root, out string message)
        {
            message = string.Empty;

            if (!IsRecordType(root, "event_msg")
                || !TryGetObject(root, "payload", out var payload)
                || !IsPayloadType(payload, "user_message")
                || !TryGetNonEmptyString(payload, "message", out message)
                || IsEnvironmentContextMessage(message))
            {
                message = string.Empty;
                return false;
            }

            return true;
        }

        private static bool TryReadAssistantReply(JsonElement root, out string message)
        {
            message = string.Empty;

            if (!IsRecordType(root, "event_msg")
                || !TryGetObject(root, "payload", out var payload)
                || !IsPayloadType(payload, "agent_message")
                || !TryGetNonEmptyString(payload, "message", out message))
            {
                message = string.Empty;
                return false;
            }

            return true;
        }

        private static bool IsPayloadType(JsonElement payload, string expectedType)
        {
            return TryGetString(payload, "type", out var type)
                && string.Equals(type, expectedType, StringComparison.Ordinal);
        }

        private static bool IsEnvironmentContextMessage(string message)
        {
            var trimmedMessage = message.TrimStart();
            return trimmedMessage.StartsWith("<environment_context>", StringComparison.Ordinal)
                && trimmedMessage.Contains("</environment_context>", StringComparison.Ordinal);
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

        private static bool TryGetObject(
            JsonElement element,
            string propertyName,
            out JsonElement property)
        {
            if (element.TryGetProperty(propertyName, out property)
                && property.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            property = default;
            return false;
        }

        private static string GetRequiredString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"{propertyName} is missing.");
            }

            var value = property.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"{propertyName} is empty.");
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

        private static DateTimeOffset GetRequiredTimestamp(JsonElement element, string propertyName)
        {
            if (!TryGetTimestamp(element, propertyName, out var timestamp))
            {
                throw new InvalidDataException($"{propertyName} is missing.");
            }

            return timestamp;
        }

        private static bool TryGetTimestamp(JsonElement element, out DateTimeOffset timestamp)
        {
            return TryGetTimestamp(element, "timestamp", out timestamp);
        }

        private static bool TryGetTimestamp(
            JsonElement element,
            string propertyName,
            out DateTimeOffset timestamp)
        {
            timestamp = default;

            if (!element.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var value = property.GetString();
            return DateTimeOffset.TryParse(value, out timestamp);
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
