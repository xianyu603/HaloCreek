using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class ConfigService
    {
        private const string AppDirectoryName = "HaloCreek";
        private const string ConfigFileName = "config.json";
        private const string WorkspaceConfigDirectoryName = ".HaloCreek";

        private static readonly JsonSerializerOptions JsonSerializerOptions = new()
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        private readonly string _globalConfigPath;

        public ConfigService()
            : this(GetDefaultGlobalConfigPath())
        {
        }

        public ConfigService(string globalConfigPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(globalConfigPath);

            _globalConfigPath = globalConfigPath;
        }

        public AppConfig LoadEffectiveConfig(string? workspacePath)
        {
            var config = ApplyConfigFile(AppConfig.DefaultForMvp1, _globalConfigPath);
            var workspaceConfigPath = TryGetWorkspaceConfigPath(workspacePath);

            return workspaceConfigPath is null
                ? config
                : ApplyConfigFile(config, workspaceConfigPath);
        }

        private static string GetDefaultGlobalConfigPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appDataPath))
            {
                appDataPath = AppContext.BaseDirectory;
            }

            return Path.Combine(appDataPath, AppDirectoryName, ConfigFileName);
        }

        private static string? TryGetWorkspaceConfigPath(string? workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return null;
            }

            try
            {
                return Path.Combine(
                    Path.GetFullPath(workspacePath.Trim()),
                    WorkspaceConfigDirectoryName,
                    ConfigFileName);
            }
            catch (Exception ex) when (ex is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                return null;
            }
        }

        private static AppConfig ApplyConfigFile(AppConfig baseConfig, string configPath)
        {
            if (!File.Exists(configPath))
            {
                return baseConfig;
            }

            try
            {
                using var stream = File.OpenRead(configPath);
                var fileConfig = JsonSerializer.Deserialize<AppConfigFile>(stream, JsonSerializerOptions);

                return fileConfig is null
                    ? baseConfig
                    : Merge(baseConfig, fileConfig);
            }
            catch (Exception ex) when (ex is JsonException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException)
            {
                return baseConfig;
            }
        }

        private static AppConfig Merge(AppConfig baseConfig, AppConfigFile fileConfig)
        {
            return new AppConfig(
                Coalesce(fileConfig.CodexExecutableName, baseConfig.CodexExecutableName),
                Coalesce(fileConfig.CodexLaunchArguments, baseConfig.CodexLaunchArguments),
                CoalescePositive(fileConfig.MaxSessionHistoryFiles, baseConfig.MaxSessionHistoryFiles),
                Coalesce(fileConfig.DiffToolPath, baseConfig.DiffToolPath),
                Coalesce(fileConfig.DefaultWorkspacePath, baseConfig.DefaultWorkspacePath));
        }

        private static string Coalesce(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
        }

        private static IReadOnlyList<string> Coalesce(
            IReadOnlyList<string?>? values,
            IReadOnlyList<string> fallback)
        {
            return values is null
                ? fallback
                : values.Where(value => value is not null).Select(value => value!).ToArray();
        }

        private static int CoalescePositive(int? value, int fallback)
        {
            return value.GetValueOrDefault() > 0
                ? value.GetValueOrDefault()
                : fallback;
        }

        private sealed class AppConfigFile
        {
            public string? CodexExecutableName { get; init; }

            public List<string?>? CodexLaunchArguments { get; init; }

            public int? MaxSessionHistoryFiles { get; init; }

            public string? DiffToolPath { get; init; }

            public string? DefaultWorkspacePath { get; init; }
        }
    }
}
