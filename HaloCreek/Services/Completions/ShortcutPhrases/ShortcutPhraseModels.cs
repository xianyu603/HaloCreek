using System;
using System.Collections.Generic;

namespace HaloCreek.Services.Completions.ShortcutPhrases
{
    public sealed record ShortcutPhraseCategory(
        string Name,
        // Category aliases are exact-match route tokens. When a query equals one of them,
        // completion shows this category's items directly.
        IReadOnlyList<string> Aliases,
        string? Description,
        IReadOnlyList<ShortcutPhraseItem> Items);

    public sealed record ShortcutPhraseItem(
        string Title,
        // Item aliases are search keywords only. They participate in fuzzy item matching,
        // but are not exact-match route tokens and are never inserted.
        IReadOnlyList<string> Aliases,
        string? Description,
        string InsertText);
}
