using System.Collections.Generic;

namespace HaloCreek.Services.WorkspacePaths
{
    internal sealed class WorkspacePathIndexSnapshot
    {
        public required string WorkspacePath { get; init; }

        public required WorkspacePathIndexDirectoryNode Root { get; init; }

        public required IReadOnlyList<WorkspacePathIndexFileNode> Files { get; init; }

        public required IReadOnlyList<WorkspacePathIndexDirectoryNode> Directories { get; init; }
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
