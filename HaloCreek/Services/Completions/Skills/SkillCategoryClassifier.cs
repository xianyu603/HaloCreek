using System;
using System.Collections.Generic;
using System.Linq;

namespace HaloCreek.Services.Completions.Skills
{
    internal static class SkillCategoryClassifier
    {
        public const string InformationCategoryName = "Info";
        public const string CodingCategoryName = "Coding";
        public const string ActionCategoryName = "Action";
        public const string ManagementCategoryName = "Management";
        public const string OtherCategoryName = "Other";

        public static readonly string[] CategoryOrder =
        [
            InformationCategoryName,
            CodingCategoryName,
            ActionCategoryName,
            ManagementCategoryName,
            OtherCategoryName,
        ];

        private static readonly IReadOnlyDictionary<string, string> CategoryDescriptions =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [InformationCategoryName] = "Docs, APIs, references, and read-only context.",
                [CodingCategoryName] = "Implementation, editing, refactoring, and code review.",
                [ActionCategoryName] = "Build, test, launch, publish, or generate assets.",
                [ManagementCategoryName] = "Create, install, configure, and manage skills/plugins.",
                [OtherCategoryName] = "Items that do not fit a stable category yet.",
            };

        public static string GetDescription(string categoryName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);

            return CategoryDescriptions[categoryName];
        }

        public static string Classify(SkillCatalogItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            var nameTokens = Tokenize(item.Name);
            var descriptionTokens = Tokenize(item.Description);

            var informationScore = ScoreInformation(nameTokens, descriptionTokens);
            var codingScore = ScoreCoding(nameTokens, descriptionTokens);
            var actionScore = ScoreAction(nameTokens, descriptionTokens);
            var managementScore = ScoreManagement(nameTokens, descriptionTokens);

            var scores = new[]
            {
                new SkillCategoryScore(InformationCategoryName, informationScore),
                new SkillCategoryScore(CodingCategoryName, codingScore),
                new SkillCategoryScore(ActionCategoryName, actionScore),
                new SkillCategoryScore(ManagementCategoryName, managementScore),
            };

            var best = scores
                .OrderByDescending(score => score.Score)
                .ThenBy(score => Array.IndexOf(CategoryOrder, score.CategoryName))
                .First();

            return best.Score > 0 ? best.CategoryName : OtherCategoryName;
        }

        private static int ScoreInformation(
            IReadOnlySet<string> nameTokens,
            IReadOnlySet<string> descriptionTokens)
        {
            var score = 0;
            score += CountMatches(nameTokens, ["docs", "documentation", "search", "api", "framework"]) * 20;
            score += CountMatches(descriptionTokens, ["docs", "documentation", "search", "api", "framework"]) * 4;
            return score;
        }

        private static int ScoreCoding(
            IReadOnlySet<string> nameTokens,
            IReadOnlySet<string> descriptionTokens)
        {
            var score = 0;
            score += CountMatches(nameTokens, ["code", "coding", "implement", "refactor"]) * 16;
            score += CountMatches(descriptionTokens, ["coding", "implement", "refactor"]) * 4;

            if (descriptionTokens.Contains("code")
                && (descriptionTokens.Contains("review")
                    || descriptionTokens.Contains("editing")
                    || descriptionTokens.Contains("writing")))
            {
                score += 6;
            }

            return score;
        }

        private static int ScoreAction(
            IReadOnlySet<string> nameTokens,
            IReadOnlySet<string> descriptionTokens)
        {
            var score = 0;
            score += CountMatches(nameTokens, ["build", "test", "run", "launch", "publish", "generate", "asset", "imagegen"]) * 20;
            score += CountMatches(descriptionTokens, ["build", "validate", "compile", "run", "launch", "publish", "generate", "asset"]) * 5;
            score += CountMatches(descriptionTokens, ["image", "images", "bitmap", "raster", "sprite", "sprites"]) * 4;
            return score;
        }

        private static int ScoreManagement(
            IReadOnlySet<string> nameTokens,
            IReadOnlySet<string> descriptionTokens)
        {
            var score = 0;
            if ((nameTokens.Contains("skill") || nameTokens.Contains("plugin"))
                && HasAny(nameTokens, ["creator", "installer", "create", "install", "manage", "config"]))
            {
                score += 35;
            }

            score += CountMatches(nameTokens, ["creator", "installer", "config", "manage"]) * 16;
            if ((descriptionTokens.Contains("skill") || descriptionTokens.Contains("skills")
                    || descriptionTokens.Contains("plugin") || descriptionTokens.Contains("plugins"))
                && HasAny(descriptionTokens, ["create", "creating", "install", "installable", "installed", "scaffold", "manifest", "config", "manage"]))
            {
                score += 12;
            }

            score += CountMatches(descriptionTokens, ["manifest", "marketplace", "scaffold", "configure", "configuration"]) * 4;
            return score;
        }

        private static IReadOnlySet<string> Tokenize(string? text)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text))
            {
                return tokens;
            }

            var tokenStart = -1;
            for (var index = 0; index < text.Length; index++)
            {
                if (char.IsLetterOrDigit(text[index]))
                {
                    if (tokenStart < 0)
                    {
                        tokenStart = index;
                    }
                }
                else if (tokenStart >= 0)
                {
                    tokens.Add(text[tokenStart..index]);
                    tokenStart = -1;
                }
            }

            if (tokenStart >= 0)
            {
                tokens.Add(text[tokenStart..]);
            }

            return tokens;
        }

        private static int CountMatches(IReadOnlySet<string> tokens, string[] keywords)
        {
            return keywords.Count(tokens.Contains);
        }

        private static bool HasAny(IReadOnlySet<string> tokens, string[] keywords)
        {
            return keywords.Any(tokens.Contains);
        }

        private sealed record SkillCategoryScore(
            string CategoryName,
            int Score);
    }
}
