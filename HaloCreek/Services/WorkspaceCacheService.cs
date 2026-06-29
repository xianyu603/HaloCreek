using System;
using System.IO;
using System.Text.Json;

namespace HaloCreek.Services
{
    public sealed class WorkspaceCacheService
    {
        private const string AppDirectoryName = "HaloCreek";
        private const string CacheFileName = "workspace-cache.json";

        private static readonly JsonSerializerOptions JsonSerializerOptions = new()
        {
            WriteIndented = true,
        };

        private readonly string _cachePath;

        public WorkspaceCacheService()
            : this(GetDefaultCachePath())
        {
        }

        public WorkspaceCacheService(string cachePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);

            _cachePath = cachePath;
        }

        public string? LoadLastWorkspacePath()
        {
            if (!File.Exists(_cachePath))
            {
                return null;
            }

            try
            {
                using var stream = File.OpenRead(_cachePath);
                var cache = JsonSerializer.Deserialize<WorkspaceCacheFile>(stream, JsonSerializerOptions);

                return string.IsNullOrWhiteSpace(cache?.LastWorkspacePath)
                    ? null
                    : cache.LastWorkspacePath.Trim();
            }
            catch (Exception ex) when (ex is JsonException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException)
            {
                return null;
            }
        }

        public bool SaveLastWorkspacePath(string workspacePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            try
            {
                var directory = Path.GetDirectoryName(_cachePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var stream = File.Create(_cachePath);
                JsonSerializer.Serialize(
                    stream,
                    new WorkspaceCacheFile { LastWorkspacePath = workspacePath.Trim() },
                    JsonSerializerOptions);
                return true;
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or PathTooLongException
                or ArgumentException)
            {
                return false;
            }
        }

        private static string GetDefaultCachePath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appDataPath))
            {
                appDataPath = AppContext.BaseDirectory;
            }

            return Path.Combine(appDataPath, AppDirectoryName, CacheFileName);
        }

        private sealed class WorkspaceCacheFile
        {
            public string? LastWorkspacePath { get; init; }
        }
    }
}
