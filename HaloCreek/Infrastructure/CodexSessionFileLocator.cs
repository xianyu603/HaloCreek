using System;
using System.IO;
using System.Linq;
using HaloCreek.Logging;

namespace HaloCreek.Infrastructure
{
    internal static class CodexSessionFileLocator
    {
        private const string LogCategory = "CodexSessions";
        private const string JsonlSearchPattern = "*.jsonl";
        private const string SessionsDirectoryName = "sessions";
        private const string CodexDirectoryName = ".codex";
        private static readonly EnumerationOptions SessionFileEnumerationOptions = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
        };

        public static string? FindSessionRootPath()
        {
            if (!PlatformInfrastructure.TryGetReadableWslHomeDirectoryPath(out var homeDirectoryPath))
            {
                return null;
            }

            var sessionRootPath = Path.Combine(
                homeDirectoryPath,
                CodexDirectoryName,
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
    }
}
