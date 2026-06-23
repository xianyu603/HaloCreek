using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;
using HaloCreek.Models;

namespace HaloCreek.Services.Completions.Files
{
    internal sealed class FileCompletionSource : ICompletionSource
    {
        public const char TriggerCharacter = '@';

        private const string LogCategory = "FileCompletion";
        private const int MaxSnapshotItems = 50;
        private const int SnapshotBatchPathCount = 50;
        private const int RecentCommitCount = 5;

        private readonly FileCompletionCandidateReader _candidateReader;

        public FileCompletionSource(FileCompletionCandidateReader candidateReader)
        {
            _candidateReader = candidateReader
                ?? throw new ArgumentNullException(nameof(candidateReader));
        }

        public async IAsyncEnumerable<CompletionQuerySnapshot> StartQuery(
            string text,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var query = text ?? string.Empty;
            if (string.IsNullOrEmpty(query))
            {
                yield return new CompletionQuerySnapshot(BuildEmptyQueryItems(), false);
                yield break;
            }

            await System.Threading.Tasks.Task.Yield();
            await foreach (var snapshot in StreamWorkspaceSnapshots(cancellationToken))
            {
                yield return snapshot;
            }
        }

        private IReadOnlyList<PromptCompletionItem> BuildEmptyQueryItems()
        {
            var groups = new List<PromptCompletionItem>();

            var uncommittedItems = _candidateReader.GetUncommittedFiles()
                .Select(BuildFileItem)
                .ToArray();
            if (uncommittedItems.Length > 0)
            {
                groups.Add(new PromptCompletionItem
                {
                    Title = "Uncommitted",
                    Children = uncommittedItems,
                });
            }

            var recentCommittedItems = _candidateReader.GetRecentCommittedFiles(RecentCommitCount)
                .Select(BuildFileItem)
                .ToArray();
            if (recentCommittedItems.Length > 0)
            {
                groups.Add(new PromptCompletionItem
                {
                    Title = "Recent committed",
                    Children = recentCommittedItems,
                });
            }

            return groups;
        }

        private async IAsyncEnumerable<CompletionQuerySnapshot> StreamWorkspaceSnapshots(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>(MaxSnapshotItems + SnapshotBatchPathCount);
            var pendingPathCount = 0;
            var workspaceFiles = _candidateReader.StreamWorkspaceFiles(cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            IReadOnlyList<PromptCompletionItem> BuildSnapshotItems()
            {
                paths.Sort(StringComparer.OrdinalIgnoreCase); // 后续接入评分排序改这里
                if (paths.Count > MaxSnapshotItems)
                {
                    paths.RemoveRange(MaxSnapshotItems, paths.Count - MaxSnapshotItems);
                }

                return paths
                    .Select(BuildFileItem)
                    .ToArray();
            }

            try
            {
                while (true)
                {
                    string relativePath;
                    try
                    {
                        if (!await workspaceFiles.MoveNextAsync())
                        {
                            break;
                        }

                        relativePath = workspaceFiles.Current;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(LogCategory, $"Failed to stream workspace file completions. {ex}");
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (!seenPaths.Add(relativePath))
                    {
                        continue;
                    }

                    paths.Add(relativePath);
                    pendingPathCount++;

                    if (pendingPathCount >= SnapshotBatchPathCount)
                    {
                        pendingPathCount = 0;
                        yield return new CompletionQuerySnapshot(BuildSnapshotItems(), true);
                    }
                }
            }
            finally
            {
                await workspaceFiles.DisposeAsync();
            }

            cancellationToken.ThrowIfCancellationRequested();
            yield return new CompletionQuerySnapshot(BuildSnapshotItems(), false);
        }

        private static PromptCompletionItem BuildFileItem(string relativePath)
        {
            return new PromptCompletionItem
            {
                Title = Path.GetFileName(
                    PlatformInfrastructure.NormalizePathForCurrentPlatform(relativePath)),
                Description = relativePath,
                InsertText = relativePath,
            };
        }
    }
}
