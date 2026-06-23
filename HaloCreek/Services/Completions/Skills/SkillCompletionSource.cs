using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using FuzzySharp;
using HaloCreek.Models;
using HaloCreek.Services.Completions;

namespace HaloCreek.Services.Completions.Skills
{
    internal sealed class SkillCompletionSource : ICompletionSource
    {
        public const char TriggerCharacter = '$';

        private const int MaxFuzzyMatchItems = 10;

        private static readonly SkillSourceKind[] SourceOrder =
        [
            SkillSourceKind.Project,
            SkillSourceKind.System,
            SkillSourceKind.User,
            SkillSourceKind.Other,
        ];

        private static readonly IReadOnlyDictionary<string, SkillSourceKind> SourceExactTokens =
            BuildSourceExactTokenIndex();

        private static readonly IReadOnlyDictionary<string, string> CategoryExactTokens =
            BuildCategoryExactTokenIndex();

        private readonly IReadOnlyList<SkillCatalogSource> _sources;

        public SkillCompletionSource(SkillCatalogReader catalogReader)
        {
            ArgumentNullException.ThrowIfNull(catalogReader);

            _sources = catalogReader.ReadCatalog();
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
            return GetSortedSources(_sources)
                .Where(source => source.Skills.Count > 0)
                .Select(source => new PromptCompletionItem
                {
                    Title = source.Source.ToString(),
                    Description = source.DirectoryPath,
                    Children = BuildCategoryItems(source.Skills),
                })
                .ToArray();
        }

        private IReadOnlyList<PromptCompletionItem> BuildQueriedItems(string query)
        {
            if (SourceExactTokens.TryGetValue(query, out var source))
            {
                var skills = GetSourceSkills(source).ToArray();
                if (skills.Length == 0)
                {
                    return Array.Empty<PromptCompletionItem>();
                }

                return BuildCategoryItems(skills);
            }

            if (CategoryExactTokens.TryGetValue(query, out var category))
            {
                return GetAllSortedSkills()
                    .Where(item => SkillCategoryClassifier.Classify(item) == category)
                    .Select(BuildSkillItem)
                    .ToArray();
            }

            var exactSkillMatches = GetAllSortedSkills()
                .Where(item => string.Equals(item.Name, query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var exactSkillMatchSet = new HashSet<SkillCatalogItem>(exactSkillMatches);
            var fuzzyMatches = GetSortedFuzzyMatches(query)
                .Where(item => !exactSkillMatchSet.Contains(item));

            return exactSkillMatches
                .Concat(fuzzyMatches)
                .Take(MaxFuzzyMatchItems)
                .Select(BuildSkillItem)
                .ToArray();
        }

        private IEnumerable<SkillCatalogItem> GetSourceSkills(SkillSourceKind source)
        {
            return GetSortedSources(_sources)
                .Where(catalogSource => catalogSource.Source == source)
                .SelectMany(catalogSource => GetSortedSkills(catalogSource.Skills));
        }

        private static IReadOnlyList<PromptCompletionItem> BuildCategoryItems(
            IEnumerable<SkillCatalogItem> skills)
        {
            var skillArray = skills.ToArray();
            return SkillCategoryClassifier.CategoryOrder
                .Select(category => new
                {
                    Category = category,
                    Skills = GetSortedSkills(skillArray.Where(item =>
                        SkillCategoryClassifier.Classify(item) == category)).ToArray(),
                })
                .Where(group => group.Skills.Length > 0)
                .Select(group => new PromptCompletionItem
                {
                    Title = group.Category,
                    Description = SkillCategoryClassifier.GetDescription(group.Category),
                    Children = group.Skills.Select(BuildSkillItem).ToArray(),
                })
                .ToArray();
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
            var searchItems = GetAllSortedSkillSearchItems()
                .ToArray();
            if (searchItems.Length == 0)
            {
                return Array.Empty<SkillCatalogItem>();
            }

            var queryItem = new SkillSearchItem(null, query);
            return Process.ExtractSorted(queryItem, searchItems, item => item.SearchText)
                .Select(match => match.Value.Value
                    ?? throw new InvalidOperationException("Skill search result is missing its item."));
        }

        private static IEnumerable<SkillCatalogItem> GetSortedSkills(IEnumerable<SkillCatalogItem> items)
        {
            return items
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
        }

        private IEnumerable<SkillCatalogItem> GetAllSortedSkills()
        {
            return GetSortedSources(_sources)
                .SelectMany(source => GetSortedSkills(source.Skills));
        }

        private IEnumerable<SkillSearchItem> GetAllSortedSkillSearchItems()
        {
            return GetSortedSources(_sources)
                .SelectMany(source => GetSortedSkills(source.Skills)
                    .Select(item => new SkillSearchItem(
                        item,
                        string.Join(
                            " ",
                            new[]
                            {
                                item.Name,
                                item.Description,
                                SkillCategoryClassifier.Classify(item),
                                source.Source.ToString(),
                                item.DirectoryPath,
                            }.Where(text => !string.IsNullOrWhiteSpace(text))))));
        }

        private static IEnumerable<SkillCatalogSource> GetSortedSources(
            IEnumerable<SkillCatalogSource> sources)
        {
            return sources
                .OrderBy(source => GetSourceRank(source.Source))
                .ThenBy(source => source.DirectoryPath, StringComparer.Ordinal);
        }

        private static int GetSourceRank(SkillSourceKind source)
        {
            var index = Array.IndexOf(SourceOrder, source);
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

        private static IReadOnlyDictionary<string, string> BuildCategoryExactTokenIndex()
        {
            var categoryByToken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string[]> addCategoryTokens = (category, aliases) =>
            {
                categoryByToken[category] = category;
                foreach (var alias in aliases)
                {
                    categoryByToken[alias] = category;
                }
            };

            addCategoryTokens(SkillCategoryClassifier.InformationCategoryName, ["info", "docs"]);
            addCategoryTokens(SkillCategoryClassifier.CodingCategoryName, ["code", "review"]);
            addCategoryTokens(SkillCategoryClassifier.ActionCategoryName, ["run", "build"]);
            addCategoryTokens(SkillCategoryClassifier.ManagementCategoryName, ["manage", "config"]);
            addCategoryTokens(SkillCategoryClassifier.OtherCategoryName, ["other"]);
            return categoryByToken;
        }

        private sealed record SkillSearchItem(
            SkillCatalogItem? Value,
            string SearchText);
    }
}
