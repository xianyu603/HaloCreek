using System.Collections.Generic;

namespace HaloCreek.Models
{
    public sealed record PromptTemplateItemGroup(
        string Title,
        IReadOnlyList<PromptTemplateItem> Items)
    {
        public bool HasItems => Items.Count > 0;
    }
}
