using System.Collections.Generic;

namespace HaloCreek.Services.Completions.Skills
{
    internal sealed record SkillCatalogItem(
        string Name,
        string? Description);

    internal sealed record SkillCatalogSource(
        SkillSourceKind Source,
        string DirectoryPath,
        IReadOnlyList<SkillCatalogItem> Skills);

    internal enum SkillSourceKind
    {
        Project,
        System,
        User,
        Other,
    }
}
