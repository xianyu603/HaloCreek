using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using FuzzySharp;
using HaloCreek.Logging;
using HaloCreek.Models;
using HaloCreek.Services.Completions;

namespace HaloCreek.Services.Completions.ShortcutPhrases
{
    public sealed class ShortcutPhraseCompletionSource : ICompletionSource
    {
        public const char TriggerCharacter = '#';

        private const string LogCategory = "Completion";
        private const int MaxFuzzyMatchItems = 10;

        private readonly IReadOnlyList<ShortcutPhraseCategory> _categories;
        private readonly IReadOnlyDictionary<string, ShortcutPhraseCategory> _categoriesByExactToken;

        public ShortcutPhraseCompletionSource()
            : this(WorkspaceRuntime.Current.EffectiveConfig.ShortcutPhraseCategories)
        {
        }

        internal ShortcutPhraseCompletionSource(IReadOnlyList<ShortcutPhraseCategory> categories)
        {
            _categories = categories ?? throw new ArgumentNullException(nameof(categories));
            _categoriesByExactToken = BuildExactCategoryIndex(categories);
        }

        public async IAsyncEnumerable<CompletionQuerySnapshot> StartQuery(
            string text,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var query = text ?? string.Empty;
            var items = string.IsNullOrEmpty(query)
                ? BuildCategoryItems()
                : BuildQueriedItems(query);

            yield return new CompletionQuerySnapshot(items);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private IReadOnlyList<PromptCompletionItem> BuildCategoryItems()
        {
            return _categories
                .Select(category => new PromptCompletionItem
                {
                    Title = category.Name,
                    Description = category.Description,
                    Children = category.Items.Select(BuildPromptCompletionItem).ToArray(),
                })
                .ToArray();
        }

        private IReadOnlyList<PromptCompletionItem> BuildQueriedItems(string query)
        {
            if (_categoriesByExactToken.TryGetValue(query, out var category))
            {
                return category.Items
                    .Select(BuildPromptCompletionItem)
                    .ToArray();
            }

            var searchItems = _categories
                .SelectMany(category => category.Items)
                .Select(item => new ShortcutPhraseSearchItem(
                    item,
                    string.Join(
                        " ",
                        new[] { item.Title, item.Description, item.InsertText }
                            .Concat(item.Aliases)
                            .Where(text => !string.IsNullOrWhiteSpace(text)))))
                .ToArray();
            if (searchItems.Length == 0)
            {
                return Array.Empty<PromptCompletionItem>();
            }

            var queryItem = new ShortcutPhraseSearchItem(null, query);
            return Process.ExtractSorted(queryItem, searchItems, item => item.SearchText)
                .Take(MaxFuzzyMatchItems)
                .Select(match => BuildPromptCompletionItem(match.Value.Value
                    ?? throw new InvalidOperationException("Shortcut phrase search result is missing its item.")))
                .ToArray();
        }

        private static PromptCompletionItem BuildPromptCompletionItem(ShortcutPhraseItem item)
        {
            return new PromptCompletionItem
            {
                Title = item.Title,
                Description = item.Description,
                InsertText = item.InsertText,
            };
        }

        private static IReadOnlyDictionary<string, ShortcutPhraseCategory> BuildExactCategoryIndex(
            IReadOnlyList<ShortcutPhraseCategory> categories)
        {
            var categoriesByToken = new Dictionary<string, ShortcutPhraseCategory>(StringComparer.OrdinalIgnoreCase);
            foreach (var category in categories)
            {
                foreach (var token in GetCategoryExactTokens(category))
                {
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        continue;
                    }

                    if (categoriesByToken.ContainsKey(token))
                    {
                        Log.Warning(
                            LogCategory,
                            $"Duplicate shortcut phrase category token ignored. Token={token}, Category={category.Name}");
                        continue;
                    }

                    categoriesByToken.Add(token, category);
                }
            }

            return categoriesByToken;
        }

        private static IEnumerable<string> GetCategoryExactTokens(ShortcutPhraseCategory category)
        {
            yield return category.Name;
            foreach (var alias in category.Aliases)
            {
                yield return alias;
            }
        }

        private sealed record ShortcutPhraseSearchItem(
            ShortcutPhraseItem? Value,
            string SearchText);
    }
}
