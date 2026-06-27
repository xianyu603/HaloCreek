using System.Collections.Generic;
using System.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    // Deprecated compatibility route. New code should call GitInfrastructure directly.
    public sealed class GitService
    {
        public GitChangesResult GetChanges()
        {
            return GitInfrastructure.GetChanges();
        }

        public IReadOnlyList<string> GetRecentCommittedFilePaths(int commitCount)
        {
            return GitInfrastructure.GetRecentCommittedFilePaths(commitCount);
        }

        public IAsyncEnumerable<string> StreamWorkspaceFilePaths(CancellationToken cancellationToken)
        {
            return GitInfrastructure.StreamWorkspaceFilePaths(cancellationToken);
        }

        public IAsyncEnumerable<string> StreamWorkspaceFilePaths(
            string workspacePath,
            CancellationToken cancellationToken)
        {
            return GitInfrastructure.StreamWorkspaceFilePaths(workspacePath, cancellationToken);
        }

        public string? GetHeadBlobId(string? relativePath)
        {
            return GitInfrastructure.GetHeadBlobId(relativePath);
        }

        public string? HashWorkingTreeFile(string? relativePath)
        {
            return GitInfrastructure.HashWorkingTreeFile(relativePath);
        }

        public string CreateTempHeadFile(string? relativePath)
        {
            return GitInfrastructure.CreateTempHeadFile(relativePath);
        }

        public void RestoreFileFromHead(string? relativePath)
        {
            GitInfrastructure.RestoreFileFromHead(relativePath);
        }
    }
}
