using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HaloCreek.Models;

namespace HaloCreek.Services.Completions
{
    public sealed class DummyCompletionSource : ICompletionSource
    {
        public const char TriggerCharacter = '#';

        private static readonly IReadOnlyList<PromptCompletionItem> Items =
        [
            new PromptCompletionItem
            {
                Title = "workspace",
                Description = "Current workspace context",
                InsertText = "workspace",
            },
            new PromptCompletionItem
            {
                Title = "changes",
                Description = "Modified Git files",
                InsertText = "changes",
            },
            new PromptCompletionItem
            {
                Title = "review",
                Description = "Current review context",
                InsertText = "review",
            },
            new PromptCompletionItem
            {
                Title = "history",
                Description = "Recent Codex session history",
                InsertText = "history",
            },
            new PromptCompletionItem
            {
                Title = "instructions",
                Description = "Repository instructions",
                InsertText = "instructions",
            },
            new PromptCompletionItem
            {
                Title = "terminal",
                Description = "Front terminal session",
                InsertText = "terminal",
            },
        ];

        public async IAsyncEnumerable<CompletionQuerySnapshot> StartQuery(
            string text,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var items = Items
                .Where(item => MatchesQuery(item, text))
                .ToArray();

            yield return new CompletionQuerySnapshot(items);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private static bool MatchesQuery(PromptCompletionItem item, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return ContainsQuery(item.Title, query)
                || ContainsQuery(item.InsertText, query)
                || ContainsQuery(item.Description, query);
        }

        private static bool ContainsQuery(string? text, string query)
        {
            return text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
