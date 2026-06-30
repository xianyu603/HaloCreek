using System.Collections.Generic;
using HaloCreek.Services;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services.WorkspacePaths
{
    internal sealed class WorkspacePathIndexSnapshot :
        IWorkspaceSnapshot<WorkspacePathIndexSnapshot>
    {
        public required WorkspacePathIndexDirectoryNode Root { get; init; }

        public required IReadOnlyList<WorkspacePathIndexFileNode> Files { get; init; }

        public required IReadOnlyList<WorkspacePathIndexDirectoryNode> Directories { get; init; }

        public static WorkspacePathIndexSnapshot CreateEmpty()
        {
            return WorkspacePathIndexSnapshotReader.CreateEmpty();
        }

        public static WorkspacePathIndexSnapshot ReadSnapshot()
        {
            return WorkspacePathIndexSnapshotReader.Read();
        }

        public static bool ContentEquals(
            WorkspacePathIndexSnapshot left,
            WorkspacePathIndexSnapshot right)
        {
            return WorkspacePathIndexSnapshotReader.ContentEquals(left, right);
        }
    }

    internal sealed class WorkspacePathIndexDirectoryNode
    {
        public required string Name { get; init; }

        public required string RelativePath { get; init; }

        public required IReadOnlyList<WorkspacePathIndexDirectoryNode> Directories { get; init; }

        public required IReadOnlyList<WorkspacePathIndexFileNode> Files { get; init; }
    }

    internal sealed class WorkspacePathIndexFileNode
    {
        public required string Name { get; init; }

        public required string RelativePath { get; init; }
    }
}
