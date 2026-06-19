using System;
using System.Collections.Generic;

namespace HaloCreek.Models
{
    public sealed class PromptCompletionItem
    {
        public required string Title { get; init; }

        public string? Description { get; init; }

        public string? InsertText { get; init; }

        public IReadOnlyList<PromptCompletionItem> Children { get; init; } = Array.Empty<PromptCompletionItem>();

        public bool HasChildren => Children.Count > 0;
    }
}
