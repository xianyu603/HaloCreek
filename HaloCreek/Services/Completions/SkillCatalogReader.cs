using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;

namespace HaloCreek.Services.Completions
{
    internal sealed class SkillCatalogReader
    {
        private const string LogCategory = "SkillCatalog";
        private const string SkillFileName = "SKILL.md";
        private const string CodexDirectoryName = ".codex";
        private const string SkillsDirectoryName = "skills";
        private const string SystemSkillsDirectoryName = ".system";

        private readonly string _workspacePath;
        private readonly string? _homeDirectoryPath;

        public SkillCatalogReader(string workspacePath, PlatformInfrastructure platformInfrastructure)
            : this(
                workspacePath,
                (platformInfrastructure ?? throw new ArgumentNullException(nameof(platformInfrastructure)))
                    .TryGetReadableWslHomeDirectoryPath(out var homeDirectoryPath)
                    ? homeDirectoryPath
                    : null)
        {
        }

        internal SkillCatalogReader(string workspacePath, string? homeDirectoryPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            _workspacePath = workspacePath;
            _homeDirectoryPath = string.IsNullOrWhiteSpace(homeDirectoryPath)
                ? null
                : homeDirectoryPath;
        }

        public IReadOnlyList<SkillCatalogSource> ReadCatalog()
        {
            var sources = new List<SkillCatalogSource>();
            foreach (var directory in GetSourceDirectories())
            {
                sources.Add(ReadSourceDirectory(directory));
            }

            LogCatalogSummary(sources);
            return sources;
        }

        private SkillCatalogSource ReadSourceDirectory(SkillSourceDirectory sourceDirectory)
        {
            var directoryPath = sourceDirectory.DirectoryPath;
            if (!Directory.Exists(directoryPath))
            {
                Log.Warning(
                    LogCategory,
                    $"Skill source directory does not exist. Source={sourceDirectory.Source}, Path={directoryPath}");
                return new SkillCatalogSource(
                    sourceDirectory.Source,
                    sourceDirectory.DirectoryPath,
                    Array.Empty<SkillCatalogItem>());
            }

            try
            {
                var skills = Directory.EnumerateDirectories(directoryPath)
                    .Where(ShouldReadSkillDirectory)
                    .Select(skillDirectory => ReadSkillDirectory(sourceDirectory, skillDirectory))
                    .Where(item => item is not null)
                    .Select(item => item!)
                    .ToArray();

                return new SkillCatalogSource(
                    sourceDirectory.Source,
                    sourceDirectory.DirectoryPath,
                    skills);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or DirectoryNotFoundException)
            {
                Log.Warning(
                    LogCategory,
                    $"Skill source directory could not be read. Source={sourceDirectory.Source}, Path={directoryPath}, Error={ex.Message}");
                return new SkillCatalogSource(
                    sourceDirectory.Source,
                    sourceDirectory.DirectoryPath,
                    Array.Empty<SkillCatalogItem>());
            }
        }

        private SkillCatalogItem? ReadSkillDirectory(
            SkillSourceDirectory sourceDirectory,
            string skillDirectoryPath)
        {
            var skillFilePath = Path.Combine(skillDirectoryPath, SkillFileName);
            if (!File.Exists(skillFilePath))
            {
                return null;
            }

            try
            {
                var metadata = ReadSkillMetadata(skillFilePath);
                var directoryName = Path.GetFileName(skillDirectoryPath);
                var name = string.IsNullOrWhiteSpace(metadata.Name)
                    ? directoryName
                    : metadata.Name.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    Log.Warning(
                        LogCategory,
                        $"Skill name could not be resolved. Source={sourceDirectory.Source}, Path={skillFilePath}");
                    return null;
                }

                return new SkillCatalogItem(
                    name,
                    string.IsNullOrWhiteSpace(metadata.Description) ? null : metadata.Description.Trim());
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or DirectoryNotFoundException)
            {
                Log.Warning(
                    LogCategory,
                    $"Skill file could not be read. Source={sourceDirectory.Source}, Path={skillFilePath}, Error={ex.Message}");
                return null;
            }
        }

        private static SkillMetadata ReadSkillMetadata(string skillFilePath)
        {
            using var reader = new StreamReader(skillFilePath);
            var firstLine = reader.ReadLine();
            if (!string.Equals(firstLine?.Trim(), "---", StringComparison.Ordinal))
            {
                return new SkillMetadata(null, null);
            }

            string? name = null;
            string? description = null;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
                {
                    break;
                }

                if (TryReadFrontmatterValue(line, "name", out var nameValue))
                {
                    name = nameValue;
                }
                else if (TryReadFrontmatterValue(line, "description", out var descriptionValue))
                {
                    description = descriptionValue;
                }
            }

            return new SkillMetadata(name, description);
        }

        private static bool TryReadFrontmatterValue(
            string line,
            string key,
            out string? value)
        {
            value = null;
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                return false;
            }

            var actualKey = line[..separatorIndex].Trim();
            if (!string.Equals(actualKey, key, StringComparison.Ordinal))
            {
                return false;
            }

            value = UnquoteYamlLikeValue(line[(separatorIndex + 1)..].Trim());
            return true;
        }

        private static string UnquoteYamlLikeValue(string value)
        {
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"')
                    || (value[0] == '\'' && value[^1] == '\'')))
            {
                return value[1..^1];
            }

            return value;
        }

        private static bool ShouldReadSkillDirectory(string skillDirectoryPath)
        {
            return !Path.GetFileName(skillDirectoryPath)
                .StartsWith(".", StringComparison.Ordinal);
        }

        private IReadOnlyList<SkillSourceDirectory> GetSourceDirectories()
        {
            var projectSkillsPath = Path.Combine(
                _workspacePath,
                CodexDirectoryName,
                SkillsDirectoryName);

            var directories = new List<SkillSourceDirectory>
            {
                new(
                    SkillSourceKind.Project,
                    projectSkillsPath),
            };

            if (_homeDirectoryPath is null)
            {
                Log.Warning(LogCategory, "Home directory is unavailable; System and User skill sources are skipped.");
                return directories;
            }

            var userSkillsPath = Path.Combine(
                _homeDirectoryPath,
                CodexDirectoryName,
                SkillsDirectoryName);

            directories.Add(new SkillSourceDirectory(
                SkillSourceKind.System,
                Path.Combine(userSkillsPath, SystemSkillsDirectoryName)));
            directories.Add(new SkillSourceDirectory(
                SkillSourceKind.User,
                userSkillsPath));

            return directories;
        }

        private static void LogCatalogSummary(IReadOnlyList<SkillCatalogSource> sources)
        {
            foreach (var source in sources)
            {
                var sourceItems = source.Skills
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                Log.Debug(
                    LogCategory,
                    $"Source={source.Source}, Path={source.DirectoryPath}, Count={sourceItems.Length}");
                foreach (var item in sourceItems)
                {
                    Log.Debug(
                        LogCategory,
                        $"Source={source.Source}, Name={item.Name}");
                }
            }
        }

        private sealed record SkillSourceDirectory(
            SkillSourceKind Source,
            string DirectoryPath);

        private sealed record SkillMetadata(string? Name, string? Description);
    }
}
