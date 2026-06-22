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

        // 这个实际上起到两个作用 1. 全体来源集合 2. 来源顺序 有点混淆之后再多考虑拆分
        private static readonly SkillSourceKind[] SourceOrder =
        [
            SkillSourceKind.Project,
            SkillSourceKind.System,
            SkillSourceKind.User,
            SkillSourceKind.Other,
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

            var query = text ?? string.Empty;
            var items = string.IsNullOrEmpty(query)
                ? BuildSourceItems()
                : BuildQueriedItems(query);

            yield return new CompletionQuerySnapshot(items, false);
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private IReadOnlyList<PromptCompletionItem> BuildSourceItems()
        {
            return SourceOrder
                .Select(source => BuildSourceItem(source))
                .Where(item => item is not null)
                .Select(item => item!)
                .ToArray();
        }

        private PromptCompletionItem? BuildSourceItem(SkillSourceKind source)
        {
            var sourceSkills = GetSourceSkills(source).ToArray();
            if (sourceSkills.Length == 0)
            {
                return null;
            }

            return new PromptCompletionItem
            {
                Title = source.ToString(),
                Children =
                [
                    new PromptCompletionItem
                    {
                        Title = FixedCategoryName,
                        Children = sourceSkills.Select(BuildSkillItem).ToArray(),
                    },
                ],
            };
        }

        private IReadOnlyList<PromptCompletionItem> BuildQueriedItems(string query)
        {
            // 这里需要在下一step 扩充规则
            return _items
                .Where(item => ContainsQuery(item.Name, query))
                .OrderBy(GetSourceRank)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(BuildSkillItem)
                .ToArray();
        }

        private IEnumerable<SkillCatalogItem> GetSourceSkills(SkillSourceKind source)
        {
            return _items
                .Where(item => item.Source == source)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
        }

        private static PromptCompletionItem BuildSkillItem(SkillCatalogItem skill)
        {
            return new PromptCompletionItem
            {
                Title = skill.Name,
                Description = FormatSkillDescription(skill),
                InsertText = TriggerCharacter + skill.Name,
            };
        }

        private static string FormatSkillDescription(SkillCatalogItem skill)
        {
            var prefix = $"{skill.Source} · {FixedCategoryName}";
            if (string.IsNullOrWhiteSpace(skill.Description))
            {
                return prefix;
            }

            // prefix 没什么信息量 直接用description就好
            return $"{skill.Description}";
        }

        private static bool ContainsQuery(string? text, string query)
        {
            return text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
        }

        private static int GetSourceRank(SkillCatalogItem item)
        {
            var index = Array.IndexOf(SourceOrder, item.Source);
            return index < 0 ? SourceOrder.Length : index;
        }
    }
}
