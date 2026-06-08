using System.Collections.Generic;

namespace HaloCreek.Models
{
    public sealed record GitChangeInfo(
        string RelativePath,
        GitChangeType ChangeType,
        bool IsStaged,
        string? OriginalRelativePath = null)
    {
        public string ChangeTypeText => ChangeType switch
        {
            GitChangeType.Added => "A",
            GitChangeType.Modified => "M",
            GitChangeType.Deleted => "D",
            GitChangeType.Renamed => "R",
            GitChangeType.Copied => "C",
            GitChangeType.Untracked => "?",
            GitChangeType.Conflicted => "U",
            _ => "?",
        };

        public string DisplayPath => string.IsNullOrWhiteSpace(OriginalRelativePath)
            ? RelativePath
            : $"{OriginalRelativePath} -> {RelativePath}";
    }

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

    public sealed record GitChangesResult(
        IReadOnlyList<GitChangeInfo> Changes,
        string Message,
        string WorkspacePath);
}
