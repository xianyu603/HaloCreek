using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using HaloCreek.Models;

namespace HaloCreek.Services.Completions
{
    internal sealed class SkillCompletionSource : ICompletionSource
    {
        public const char TriggerCharacter = '$';

        private const string FixedCategoryName = "其他";

        private static readonly SkillSourceKind[] SourceOrder =
        [
            SkillSourceKind.Project,
            SkillSourceKind.System,
            SkillSourceKind.User,
            SkillSourceKind.Other,
        ];

        private static readonly IReadOnlyDictionary<string, SkillSourceKind> SourceExactTokens =
            BuildSourceExactTokenIndex();

        private static readonly string[] FixedCategoryExactTokens =
        [
            FixedCategoryName,
            "other",
        ];

        private readonly IReadOnlyList<SkillCatalogItem> _items;

        public SkillCompletionSource(SkillCatalogReader catalogReader)
        {
            ArgumentNullException.ThrowIfNull(catalogReader);

            _items = catalogReader.ReadCatalog();
        }

        public async IAsyncEnumerable<CompletionQuerySnapshot> StartQuery(
            string text,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var query = (text ?? string.Empty).Trim();
            var items = string.IsNullOrEmpty(query)
                ? BuildSourceItems()
                : BuildQueriedItems(query);

            yield return new CompletionQuerySnapshot(items, false);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private IReadOnlyList<PromptCompletionItem> BuildSourceItems()
        {
            return SourceOrder
                .Select(source => new
                {
                    Source = source,
                    Skills = GetSourceSkills(source).ToArray(),
                })
                .Where(group => group.Skills.Length > 0)
                .Select(group => new PromptCompletionItem
                {
                    Title = group.Source.ToString(),
                    Children =
                    [
                        new PromptCompletionItem
                        {
                            Title = FixedCategoryName,
                            Children = group.Skills.Select(BuildSkillItem).ToArray(),
                        },
                    ],
                })
                .ToArray();
        }

        private IReadOnlyList<PromptCompletionItem> BuildQueriedItems(string query)
        {
            if (FixedCategoryExactTokens.Contains(query, StringComparer.OrdinalIgnoreCase))
            {
                return GetSortedSkills(_items)
                    .Select(BuildSkillItem)
                    .ToArray();
            }

            if (SourceExactTokens.TryGetValue(query, out var source))
            {
                var skills = GetSourceSkills(source).ToArray();
                if (skills.Length == 0)
                {
                    return Array.Empty<PromptCompletionItem>();
                }

                return
                [
                    new PromptCompletionItem
                    {
                        Title = FixedCategoryName,
                        Children = skills.Select(BuildSkillItem).ToArray(),
                    },
                ];
            }

            var exactSkillMatches = GetSortedSkills(_items)
                .Where(item => string.Equals(item.Name, query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var exactSkillMatchSet = new HashSet<SkillCatalogItem>(exactSkillMatches);
            var fuzzyMatches = GetSortedFuzzyMatches(query)
                .Where(item => !exactSkillMatchSet.Contains(item));

            return exactSkillMatches
                .Concat(fuzzyMatches)
                .Select(BuildSkillItem)
                .ToArray();
        }

        private IEnumerable<SkillCatalogItem> GetSourceSkills(SkillSourceKind source)
        {
            return GetSortedSkills(_items.Where(item => item.Source == source));
        }

        private static PromptCompletionItem BuildSkillItem(SkillCatalogItem skill)
        {
            return new PromptCompletionItem
            {
                Title = skill.Name,
                Description = skill.Description,
                InsertText = TriggerCharacter + skill.Name,
            };
        }

        private IEnumerable<SkillCatalogItem> GetSortedFuzzyMatches(string query)
        {
            return _items
                .Select(item => new
                {
                    Item = item,
                    Rank = GetMatchRank(item, query),
                })
                .Where(match => match.Rank is not null)
                .OrderBy(match => match.Rank)
                .ThenBy(match => GetSourceRank(match.Item))
                .ThenBy(match => match.Item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(match => match.Item);
        }

        private static int? GetMatchRank(SkillCatalogItem item, string query)
        {
            Func<string?, bool> containsQuery = text =>
                text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

            if (containsQuery(item.Name))
            {
                return 0;
            }

            if (containsQuery(item.Description))
            {
                return 1;
            }

            if (containsQuery(item.Source.ToString())
                || containsQuery(FixedCategoryName))
            {
                return 2;
            }

            return null;
        }

        private static IEnumerable<SkillCatalogItem> GetSortedSkills(IEnumerable<SkillCatalogItem> items)
        {
            return items
                .OrderBy(GetSourceRank)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
        }

        private static int GetSourceRank(SkillCatalogItem item)
        {
            var index = Array.IndexOf(SourceOrder, item.Source);
            return index < 0 ? SourceOrder.Length : index;
        }

        // 下面两个只服务于初始化 未来考虑拆个类
        private static IReadOnlyDictionary<string, SkillSourceKind> BuildSourceExactTokenIndex()
        {
            var sourceByToken = new Dictionary<string, SkillSourceKind>(StringComparer.OrdinalIgnoreCase);
            Action<SkillSourceKind, string[]> addSourceTokens = (source, aliases) =>
            {
                sourceByToken[source.ToString()] = source;
                foreach (var alias in aliases)
                {
                    sourceByToken[alias] = source;
                }
            };

            addSourceTokens(SkillSourceKind.Project, ["project", "workspace"]);
            addSourceTokens(SkillSourceKind.System, ["system", "builtin"]);
            addSourceTokens(SkillSourceKind.User, ["user", "personal"]);
            addSourceTokens(SkillSourceKind.Other, ["other"]);
            return sourceByToken;
        }
    }
}
