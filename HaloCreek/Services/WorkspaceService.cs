using System;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class WorkspaceService
    {
        public WorkspaceInfo? CurrentWorkspace { get; private set; }

        public WorkspaceInfo CreateWorkspace(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            return new WorkspaceInfo(path.Trim());
        }

        public void SetCurrentWorkspace(WorkspaceInfo workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            CurrentWorkspace = workspace;
        }

        public WorkspaceInfo? GetCurrentWorkspace()
        {
            return CurrentWorkspace;
        }
    }
}
