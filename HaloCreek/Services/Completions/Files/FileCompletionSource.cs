using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
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
                yield return new CompletionQuerySnapshot(
                    BuildSnapshotItems(snapshot, query),
                    true);
            }
        }

        private static IReadOnlyList<PromptCompletionItem> BuildSnapshotItems(
            WorkspacePathIndexSnapshot snapshot,
            string query)
        {
            return snapshot.Files
                .Select(file => new
                {
                    file.RelativePath,
                    Score = GetFileMatchScore(file.RelativePath, query),
                })
                .OrderByDescending(file => file.Score)
                .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
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
    }
}
