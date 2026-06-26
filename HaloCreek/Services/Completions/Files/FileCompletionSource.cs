using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FuzzySharp;
using HaloCreek.Infrastructure;
using HaloCreek.Models;
using HaloCreek.Services.WorkspacePaths;

namespace HaloCreek.Services.Completions.Files
{
    internal sealed class FileCompletionSource : ICompletionSource
    {
        public const char TriggerCharacter = '@';

        private const int MaxSnapshotItems = 10;
        private const int RecentCommitCount = 5;

        private readonly FileCompletionCandidateReader _candidateReader;
        private readonly WorkspacePathIndexService _workspacePathIndexService;

        public FileCompletionSource(
            FileCompletionCandidateReader candidateReader,
            WorkspacePathIndexService workspacePathIndexService)
        {
            _candidateReader = candidateReader
                ?? throw new ArgumentNullException(nameof(candidateReader));
            _workspacePathIndexService = workspacePathIndexService
                ?? throw new ArgumentNullException(nameof(workspacePathIndexService));
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
            await foreach (var snapshot in StreamWorkspaceSnapshots(query, cancellationToken))
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
            string query,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var snapshot in _workspacePathIndexService.Watch(cancellationToken))
            {
                var items = await Task.Run(
                    () =>
                    {
                        try
                        {
                            return BuildSnapshotItems(snapshot, query, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            return null;
                        }
                    },
                    CancellationToken.None).ConfigureAwait(false);

                if (items is null)
                {
                    yield break;
                }

                yield return new CompletionQuerySnapshot(
                    items,
                    true);
            }
        }

        private static IReadOnlyList<PromptCompletionItem> BuildSnapshotItems(
            WorkspacePathIndexSnapshot snapshot,
            string query,
            CancellationToken cancellationToken)
        {
            var files = new List<FileMatch>(snapshot.Files.Count);
            for (var index = 0; index < snapshot.Files.Count; index++)
            {
                if (index % 128 == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var file = snapshot.Files[index];
                files.Add(new FileMatch(
                    file.RelativePath,
                    GetFileMatchScore(file.RelativePath, query)));
            }

            cancellationToken.ThrowIfCancellationRequested();
            files.Sort(
                (left, right) =>
                {
                    var scoreComparison = right.Score.CompareTo(left.Score);
                    return scoreComparison != 0
                        ? scoreComparison
                        : StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
                });

            cancellationToken.ThrowIfCancellationRequested();
            return files
                .Take(MaxSnapshotItems)
                .Select(file => BuildFileItem(file.RelativePath))
                .ToArray();
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

        private static int GetFileMatchScore(string relativePath, string query)
        {
            var fileName = Path.GetFileName(
                PlatformInfrastructure.NormalizePathForCurrentPlatform(relativePath));
            return Math.Max(
                Fuzz.WeightedRatio(query, fileName),
                Fuzz.WeightedRatio(query, relativePath));
        }

        private sealed record FileMatch(
            string RelativePath,
            int Score);
    }
}
