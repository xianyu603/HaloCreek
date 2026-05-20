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

        public CodexSessionHistoryReader(PlatformInfrastructure platformInfrastructure)
        {
            _platformInfrastructure = platformInfrastructure ?? throw new ArgumentNullException(nameof(platformInfrastructure));
        }

        public SessionHistoryResult ReadSessions(string? workspacePath, AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (string.IsNullOrWhiteSpace(workspacePath) || config.MaxSessionHistoryFiles <= 0)
            {
                return new SessionHistoryResult(Array.Empty<HistorySessionInfo>(), 0);
            }

            var sessionHistoryRootPath = FindSessionHistoryRootPath();
            if (sessionHistoryRootPath is null)
            {
                return new SessionHistoryResult(Array.Empty<HistorySessionInfo>(), 0);
            }

            var sessions = new List<HistorySessionInfo>();
            var skippedFileCount = 0;
            foreach (var sessionFilePath in EnumerateLatestSessionFiles(
                sessionHistoryRootPath,
                config.MaxSessionHistoryFiles))
            {
                try
                {
                    var session = ReadSessionFile(sessionFilePath, workspacePath);
                    if (session is not null)
                    {
                        sessions.Add(session);
                    }
                }
                catch (Exception ex) when (ex is JsonException
                    or IOException
                    or InvalidDataException
                    or NotSupportedException
                    or UnauthorizedAccessException)
                {
                    skippedFileCount++;
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

        private static IEnumerable<string> EnumerateLatestSessionFiles(
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
                    .Select(file => file.FullName)
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException
                or NotSupportedException
                or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static HistorySessionInfo? ReadSessionFile(string sessionFilePath, string workspacePath)
        {
            string? id = null;
            string? sessionWorkspacePath = null;
            DateTimeOffset? createdAt = null;
            DateTimeOffset? lastUpdatedAt = null;
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

            if (!PlatformInfrastructure.AreWorkspacePathsEquivalent(sessionWorkspacePath, workspacePath))
            {
                return null;
            }

            return new HistorySessionInfo(
                id,
                sessionWorkspacePath,
                createdAt.Value,
                lastUpdatedAt.Value,
                string.Empty,
                string.Empty,
                string.Empty,
                sessionFilePath);
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
    }
}
