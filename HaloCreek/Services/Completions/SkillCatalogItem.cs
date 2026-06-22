namespace HaloCreek.Services.Completions
{
    internal sealed record SkillCatalogItem(
        string Name,
        string? Description,
        SkillSourceKind Source);

    internal enum SkillSourceKind
    {
        Project,
        System,
        User,
        Other,
    }
}
