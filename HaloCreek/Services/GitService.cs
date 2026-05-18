using System;
using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class GitService
    {
        public IReadOnlyList<GitChangeInfo> GetChanges(WorkspaceInfo? workspace)
        {
            return Array.Empty<GitChangeInfo>();
        }

        public bool TryOpenDiffTool(WorkspaceInfo workspace, GitChangeInfo change)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(change);

            return false;
        }

        public bool TryCommit(WorkspaceInfo workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            return false;
        }
    }
}
