using System;
using System.Collections.Generic;
using HaloCreek.Services.Completions.ShortcutPhrases;
using HaloCreek.Services.PromptTemplates;

namespace HaloCreek.Models
{
    public sealed record AppConfig(
        string CodexExecutableName,
        IReadOnlyList<string> CodexLaunchArguments,
        int MaxSessionHistoryFiles,
        IReadOnlyDictionary<string, IReadOnlyList<string>> MarkdownLineJumpCommands,
        IReadOnlyList<ShortcutPhraseCategory> ShortcutPhraseCategories,
        IReadOnlyList<PromptTemplateItem> PromptTemplateItems)
    {
        public static AppConfig DefaultForMvp1 { get; } = new(
            "codex",
            Array.Empty<string>(),
            100,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["devenv"] = new[] { "/edit", "{Path}", "/command", "Edit.GoTo {Line}" },
                ["code"] = new[] { "--goto", "{Path}:{Line}" },
                ["rider64"] = new[] { "--line", "{Line}", "{Path}" },
            },
            ShortcutPhraseStaticConfig.Categories,
            PromptTemplateStaticConfig.Items);
    }
}
