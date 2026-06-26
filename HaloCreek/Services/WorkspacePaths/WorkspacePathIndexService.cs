using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;

namespace HaloCreek.Services.WorkspacePaths
{
    internal sealed class WorkspacePathIndexService : IDisposable
    {
        private const string LogCategory = "WorkspacePathIndex";
        private const int DebugSamplePathCount = 5;
        private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(10);

        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

        private readonly object _lock = new();
        private readonly GitService _gitService;
        private readonly Timer _scanTimer;
        private WorkspacePathIndexSnapshot _snapshot = CreateEmptySnapshot(string.Empty);
        private CancellationTokenSource? _buildCancellation;
        private Task? _buildTask;
        private string _workspacePath = string.Empty;
        private int _generation;
        private bool _isDisposed;

        public WorkspacePathIndexService(GitService gitService)
        {
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            _scanTimer = new Timer(
                OnScanTimerTick,
                state: null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            WorkspaceRuntime.Changed += OnWorkspaceChanged;
            ApplyWorkspace(WorkspaceRuntime.Current);
        }

        public WorkspacePathIndexSnapshot Snapshot
        {
            get
            {
                lock (_lock)
                {
                    return _snapshot;
                }
            }
        }

        public void Dispose()
        {
            CancellationTokenSource? cancellation;

            lock (_lock)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                cancellation = _buildCancellation;
                _buildCancellation = null;
                _scanTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            WorkspaceRuntime.Changed -= OnWorkspaceChanged;
            TryCancel(cancellation);
            _scanTimer.Dispose();
        }

        private void OnWorkspaceChanged(WorkspaceContext workspaceContext)
        {
            ApplyWorkspace(workspaceContext);
        }

        private void OnScanTimerTick(object? state)
        {
            lock (_lock)
            {
                if (_isDisposed)
                {
                    return;
                }

                EnsureBuildStartedLocked("timer");
            }
        }

        private void ApplyWorkspace(WorkspaceContext workspaceContext)
        {
            ArgumentNullException.ThrowIfNull(workspaceContext);

            CancellationTokenSource? oldCancellation;

            lock (_lock)
            {
                if (_isDisposed)
                {
                    return;
                }

                oldCancellation = _buildCancellation;
                _buildCancellation = null;
                _buildTask = null;
                _generation++;
                _workspacePath = workspaceContext.GitRootPath;
                _snapshot = CreateEmptySnapshot(_workspacePath);

                Log.Debug(
                    LogCategory,
                    $"Workspace applied. Generation={_generation}, Workspace={_workspacePath}");

                EnsureBuildStartedLocked("workspace");
                _scanTimer.Change(ScanInterval, ScanInterval);
            }

            TryCancel(oldCancellation);
        }

        private void EnsureBuildStartedLocked(string reason)
        {
            if (_buildTask is not null && !_buildTask.IsCompleted)
            {
                return;
            }

            var buildCancellation = new CancellationTokenSource();
            var generation = _generation;
            var workspacePath = _workspacePath;
            var cancellationToken = buildCancellation.Token;
            var buildTask = Task.Run(
                () => BuildAndPublishAsync(workspacePath, generation, reason, cancellationToken),
                CancellationToken.None);
            _buildCancellation = buildCancellation;
            _buildTask = buildTask;
            _ = buildTask.ContinueWith(
                _ =>
                {
                    lock (_lock)
                    {
                        if (ReferenceEquals(_buildTask, buildTask))
                        {
                            _buildCancellation = null;
                        }
                    }

                    buildCancellation.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task BuildAndPublishAsync(
            string workspacePath,
            int generation,
            string reason,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            Log.Debug(
                LogCategory,
                $"Build started. Reason={reason}, Generation={generation}, Workspace={workspacePath}");

            try
            {
                var snapshot = await BuildSnapshotAsync(workspacePath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                lock (_lock)
                {
                    if (_isDisposed
                        || generation != _generation
                        || !string.Equals(workspacePath, _workspacePath, StringComparison.Ordinal))
                    {
                        Log.Debug(
                            LogCategory,
                            $"Build discarded. Generation={generation}, Workspace={workspacePath}");
                        return;
                    }

                    if (!HasPathContentChanged(_snapshot, snapshot))
                    {
                        Log.Debug(
                            LogCategory,
                            $"Build completed with no changes. Generation={generation}, Files={snapshot.Files.Count}, Directories={snapshot.Directories.Count}, ElapsedMs={stopwatch.ElapsedMilliseconds}");
                        return;
                    }

                    _snapshot = snapshot;
                    Log.Debug(
                        LogCategory,
                        $"Snapshot published. Generation={generation}, Files={snapshot.Files.Count}, Directories={snapshot.Directories.Count}, RootDirectories={snapshot.Root.Directories.Count}, RootFiles={snapshot.Root.Files.Count}, ElapsedMs={stopwatch.ElapsedMilliseconds}");
                    LogSnapshotSamples(snapshot);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Log.Debug(
                    LogCategory,
                    $"Build canceled. Generation={generation}, Workspace={workspacePath}");
            }
            catch (Exception ex)
            {
                Log.Warning(
                    LogCategory,
                    $"Build failed. Generation={generation}, Workspace={workspacePath}, Error={ex}");
            }
        }

        private async Task<WorkspacePathIndexSnapshot> BuildSnapshotAsync(
            string workspacePath,
            CancellationToken cancellationToken)
        {
            var normalizedPaths = new HashSet<string>(PathComparer);

            await foreach (var relativePath in _gitService.StreamWorkspaceFilePaths(
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
                WorkspacePath = workspacePath,
                Root = root,
                Files = flatFiles
                    .OrderBy(file => file.RelativePath, PathComparer)
                    .ToArray(),
                Directories = flatDirectories
                    .OrderBy(directory => directory.RelativePath, PathComparer)
                    .ToArray(),
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

        private static bool HasPathContentChanged(
            WorkspacePathIndexSnapshot oldSnapshot,
            WorkspacePathIndexSnapshot newSnapshot)
        {
            if (!string.Equals(
                    oldSnapshot.WorkspacePath,
                    newSnapshot.WorkspacePath,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (oldSnapshot.Files.Count != newSnapshot.Files.Count)
            {
                return true;
            }

            for (var index = 0; index < oldSnapshot.Files.Count; index++)
            {
                if (!string.Equals(
                        oldSnapshot.Files[index].RelativePath,
                        newSnapshot.Files[index].RelativePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static WorkspacePathIndexSnapshot CreateEmptySnapshot(string workspacePath)
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
                WorkspacePath = workspacePath,
                Root = root,
                Files = Array.Empty<WorkspacePathIndexFileNode>(),
                Directories = Array.Empty<WorkspacePathIndexDirectoryNode>(),
            };
        }

        private static void TryCancel(CancellationTokenSource? cancellation)
        {
            if (cancellation is null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static void LogSnapshotSamples(WorkspacePathIndexSnapshot snapshot)
        {
            Log.Debug(
                LogCategory,
                "Sample files: " + FormatSample(snapshot.Files.Select(file => file.RelativePath)));
            Log.Debug(
                LogCategory,
                "Sample directories: " + FormatSample(snapshot.Directories.Select(directory => directory.RelativePath)));
        }

        private static string FormatSample(IEnumerable<string> paths)
        {
            var sample = paths
                .Take(DebugSamplePathCount)
                .ToArray();

            return sample.Length == 0
                ? "(empty)"
                : string.Join(", ", sample);
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
