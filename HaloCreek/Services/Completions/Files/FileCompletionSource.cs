using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Models;

namespace HaloCreek.Services.Completions.Files
{
    internal sealed class FileCompletionSource : ICompletionSource
    {
        public const char TriggerCharacter = '@';

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
            var items = string.IsNullOrEmpty(query)
                ? BuildEmptyQueryItems()
                : Array.Empty<PromptCompletionItem>();

            yield return new CompletionQuerySnapshot(items, false);
            await System.Threading.Tasks.Task.CompletedTask;
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
