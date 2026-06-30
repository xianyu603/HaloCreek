using System;
using System.Collections.Generic;
using HaloCreek.Services.Completions.ShortcutPhrases;

namespace HaloCreek.Models
{
    public sealed record AppConfig(
        string CodexExecutableName,
        IReadOnlyList<string> CodexLaunchArguments,
        int MaxSessionHistoryFiles,
        IReadOnlyList<ShortcutPhraseCategory> ShortcutPhraseCategories)
    {
        public static AppConfig DefaultForMvp1 { get; } = new(
            "codex",
            Array.Empty<string>(),
            100,
            ShortcutPhraseStaticConfig.Categories);
    }
}
