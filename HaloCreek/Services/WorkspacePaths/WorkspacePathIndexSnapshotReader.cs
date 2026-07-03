using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Services;

namespace HaloCreek.Services.WorkspacePaths
{
    internal static class WorkspacePathIndexSnapshotReader
    {
        private const string LogCategory = "WorkspacePathIndex";

        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

        public static WorkspacePathIndexSnapshot CreateEmpty()
        {
            var root = new WorkspacePathIndexDirectoryNode
            {
                Name = string.Empty,
                RelativePath = string.Empty,
                Directories = Array.Empty<WorkspacePathIndexDirectoryNode>(),
                Files = Array.Empty<WorkspacePathIndexFileNode>(),
            };

            return new WorkspacePathIndexSnapshot
            {
                Root = root,
                Files = Array.Empty<WorkspacePathIndexFileNode>(),
                Directories = Array.Empty<WorkspacePathIndexDirectoryNode>(),
            };
        }

        public static WorkspacePathIndexSnapshot Read()
        {
            return ReadAsync(WorkspaceRuntime.Current.WorkspacePath, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        public static bool ContentEquals(
            WorkspacePathIndexSnapshot left,
            WorkspacePathIndexSnapshot right)
        {
            if (left.Files.Count != right.Files.Count)
            {
                return false;
            }

            for (var index = 0; index < left.Files.Count; index++)
            {
                if (!string.Equals(
                        left.Files[index].RelativePath,
                        right.Files[index].RelativePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static async Task<WorkspacePathIndexSnapshot> ReadAsync(
            string workspacePath,
            CancellationToken cancellationToken)
        {
            var normalizedPaths = new HashSet<string>(PathComparer);

            await foreach (var relativePath in GitInfrastructure.StreamWorkspaceFilePaths(
                workspacePath,
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string normalizedPath;
                try
                {
                    normalizedPath = PlatformInfrastructure.NormalizeGitRelativePath(relativePath);
                }
                catch (ArgumentException ex)
                {
                    Log.Warning(
                        LogCategory,
                        $"Invalid Git relative path ignored. Path={relativePath}, Error={ex.Message}");
                    continue;
                }

                if (PlatformInfrastructure.IsExistingFileUnderDirectory(workspacePath, normalizedPath))
                {
                    normalizedPaths.Add(normalizedPath);
                }
            }

            var sortedPaths = normalizedPaths
                .OrderBy(relativePath => relativePath, PathComparer)
                .ToArray();

            return MaterializeSnapshot(workspacePath, sortedPaths);
        }

        private static WorkspacePathIndexSnapshot MaterializeSnapshot(
            string workspacePath,
            IReadOnlyList<string> sortedFilePaths)
        {
            var rootBuilder = new DirectoryBuilder(string.Empty, string.Empty);
            var directoriesByRelativePath = new Dictionary<string, DirectoryBuilder>(PathComparer)
            {
                [string.Empty] = rootBuilder,
            };

            foreach (var relativePath in sortedFilePaths)
            {
                var segments = relativePath.Split('/');
                var parent = rootBuilder;
                var directoryPath = string.Empty;

                for (var index = 0; index < segments.Length - 1; index++)
                {
                    var name = segments[index];
                    directoryPath += name + "/";

                    if (!directoriesByRelativePath.TryGetValue(directoryPath, out var directory))
                    {
                        directory = new DirectoryBuilder(name, directoryPath);
                        directoriesByRelativePath.Add(directoryPath, directory);
                        parent.Directories.Add(directory);
                    }

                    parent = directory;
                }

                parent.Files.Add(new WorkspacePathIndexFileNode
                {
                    Name = segments[^1],
                    RelativePath = relativePath,
                });
            }

            var flatDirectories = new List<WorkspacePathIndexDirectoryNode>(
                directoriesByRelativePath.Count);
            var flatFiles = new List<WorkspacePathIndexFileNode>(sortedFilePaths.Count);
            var root = MaterializeDirectory(rootBuilder, flatDirectories, flatFiles);

            return new WorkspacePathIndexSnapshot
            {
                Root = root,
                Files = flatFiles
                    .OrderBy(file => file.RelativePath, PathComparer)
                    .ToArray(),
                Directories = flatDirectories
                    .OrderBy(directory => directory.RelativePath, PathComparer)
                    .ToArray(),
                SnapshotListenPath = workspacePath,
            };
        }

        private static WorkspacePathIndexDirectoryNode MaterializeDirectory(
            DirectoryBuilder builder,
            List<WorkspacePathIndexDirectoryNode> flatDirectories,
            List<WorkspacePathIndexFileNode> flatFiles)
        {
            builder.Directories.Sort(
                (left, right) => PathComparer.Compare(left.RelativePath, right.RelativePath));
            builder.Files.Sort(
                (left, right) => PathComparer.Compare(left.RelativePath, right.RelativePath));

            var childDirectories = builder.Directories
                .Select(directory => MaterializeDirectory(
                    directory,
                    flatDirectories,
                    flatFiles))
                .ToArray();
            var files = builder.Files.ToArray();

            flatFiles.AddRange(files);

            var node = new WorkspacePathIndexDirectoryNode
            {
                Name = builder.Name,
                RelativePath = builder.RelativePath,
                Directories = childDirectories,
                Files = files,
            };

            flatDirectories.Add(node);

            return node;
        }

        private sealed class DirectoryBuilder
        {
            public DirectoryBuilder(string name, string relativePath)
            {
                Name = name;
                RelativePath = relativePath;
            }

            public string Name { get; }

            public string RelativePath { get; }

            public List<DirectoryBuilder> Directories { get; } = new();

            public List<WorkspacePathIndexFileNode> Files { get; } = new();
        }
    }
}
