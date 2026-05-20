namespace HaloCreek.Models
{
    public sealed record GitChangeInfo(
        string RelativePath,
        GitChangeType ChangeType,
        bool IsStaged,
        string? OriginalRelativePath = null);

    public enum GitChangeType
    {
        Unknown,
        Added,
        Modified,
        Deleted,
        Renamed,
        Copied,
        Untracked,
        Conflicted
    }
}
