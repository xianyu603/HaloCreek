using System;
using System.Collections.Generic;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class GitService
    {
        public IReadOnlyList<GitChangeInfo> GetChanges(string? workspacePath)
        {
            return Array.Empty<GitChangeInfo>();
        }

        public bool TryOpenDiffTool(string workspacePath, GitChangeInfo change)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
            ArgumentNullException.ThrowIfNull(change);

            return false;
        }

        public bool TryCommit(string workspacePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
            return false;
        }
    }
}
