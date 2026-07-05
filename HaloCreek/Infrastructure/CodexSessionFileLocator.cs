using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using HaloCreek.Logging;

namespace HaloCreek.Infrastructure
{
    internal static class CodexSessionFileLocator
    {
        private const string LogCategory = "CodexSessions";
        private const string JsonlSearchPattern = "*.jsonl";
        private const string SessionsDirectoryName = "sessions";
        private const string HistoryFileName = "history.jsonl";
        private static readonly EnumerationOptions SessionFileEnumerationOptions = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
        };

        public static CodexHistorySnapshot CaptureHistorySnapshot()
        {
            var historyFilePath = FindHistoryFilePath();
            if (historyFilePath is null)
            {
                return new CodexHistorySnapshot(null, 0);
            }

            try
            {
                return new CodexHistorySnapshot(
                    historyFilePath,
                    File.Exists(historyFilePath)
                        ? new FileInfo(historyFilePath).Length
                        : 0);
            }
            catch (Exception ex) when (ex is IOException
                or NotSupportedException
                or UnauthorizedAccessException)
            {
                Log.Warning(
                    LogCategory,
                    $"Codex history snapshot failed. Path={historyFilePath}, Error={ex.Message}");
                return new CodexHistorySnapshot(historyFilePath, 0);
            }
        }

        public static CodexHistoryEntry? FindNewHistoryEntry(
            CodexHistorySnapshot snapshot,
            string promptText)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(promptText);

            if (string.IsNullOrWhiteSpace(snapshot.FilePath)
                || !File.Exists(snapshot.FilePath))
            {
                return null;
            }

            var expectedText = NormalizeHistoryText(promptText);
            foreach (var line in ReadNewHistoryLines(snapshot))
            {
                if (!TryReadHistoryEntry(line, out var entry)
                    || !string.Equals(
                        NormalizeHistoryText(entry.Text),
                        expectedText,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                return entry;
            }

            return null;
        }

        public static string? FindSessionRootPath()
        {
            if (!PlatformInfrastructure.TryGetCodexDirectoryPath(out var codexDirectoryPath))
            {
                return null;
            }

            var sessionRootPath = Path.Combine(
                codexDirectoryPath,
                SessionsDirectoryName);
            if (!Directory.Exists(sessionRootPath))
            {
                Log.Warning(
                    LogCategory,
                    $"Codex session directory does not exist. Path={sessionRootPath}");
                return null;
            }

            return sessionRootPath;
        }

        // TODO 这里感觉可能会卡 如有可能结合session 创建时间来搞
        public static string? FindSessionFilePath(string sessionId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

            var sessionRootPath = FindSessionRootPath();
            if (sessionRootPath is null)
            {
                return null;
            }

            var expectedFileNameSuffix = $"-{sessionId}.jsonl";
            return Directory
                .EnumerateFiles(
                    sessionRootPath,
                    JsonlSearchPattern,
                    SessionFileEnumerationOptions)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault(file => file.Name.EndsWith(
                    expectedFileNameSuffix,
                    StringComparison.Ordinal))?
                .FullName;
        }

        private static string? FindHistoryFilePath()
        {
            if (!PlatformInfrastructure.TryGetCodexDirectoryPath(out var codexDirectoryPath))
            {
                return null;
            }

            return Path.Combine(codexDirectoryPath, HistoryFileName);
        }

        private static string[] ReadNewHistoryLines(CodexHistorySnapshot snapshot)
        {
            try
            {
                using var stream = PlatformInfrastructure.OpenFileForReadWithWriteSharing(snapshot.FilePath!);
                if (snapshot.Length > 0 && stream.Length >= snapshot.Length)
                {
                    stream.Seek(snapshot.Length, SeekOrigin.Begin);
                }

                using var reader = new StreamReader(stream);
                return reader.ReadToEnd()
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }
            catch (Exception ex) when (ex is IOException
                or NotSupportedException
                or UnauthorizedAccessException)
            {
                Log.Warning(
                    LogCategory,
                    $"Codex history diff read failed. Path={snapshot.FilePath}, Error={ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static bool TryReadHistoryEntry(string line, out CodexHistoryEntry entry)
        {
            entry = default!;

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!TryGetNonEmptyString(root, "session_id", out var sessionId)
                    || !TryGetString(root, "text", out var text))
                {
                    return false;
                }

                entry = new CodexHistoryEntry(sessionId, text);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
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

        private static bool TryGetNonEmptyString(
            JsonElement element,
            string propertyName,
            out string value)
        {
            return TryGetString(element, propertyName, out value)
                && !string.IsNullOrWhiteSpace(value);
        }

        private static string NormalizeHistoryText(string text)
        {
            return text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
        }
    }

    internal sealed record CodexHistorySnapshot(
        string? FilePath,
        long Length);

    internal sealed record CodexHistoryEntry(
        string SessionId,
        string Text);
}
