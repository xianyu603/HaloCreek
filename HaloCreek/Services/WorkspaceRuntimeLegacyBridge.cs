using System;

namespace HaloCreek.Services
{
    // 这个类本轮迁移之后需要删除
    public sealed class WorkspaceRuntimeLegacyBridge
    {
        private readonly WorkspaceRuntimeService _workspaceRuntimeService;

        public WorkspaceRuntimeLegacyBridge(WorkspaceRuntimeService workspaceRuntimeService)
        {
            _workspaceRuntimeService = workspaceRuntimeService
                ?? throw new ArgumentNullException(nameof(workspaceRuntimeService));
        }

        public WorkspaceContext SwitchWorkspace(string requestedPath)
        {
            var context = WorkspaceRuntime.SwitchWorkspace(requestedPath);
            if (!_workspaceRuntimeService.SetWorkspacePath(context.WorkspacePath))
            {
                throw new InvalidOperationException(
                    "Validated workspace could not be applied to the legacy workspace runtime.");
            }

            return context;
        }
    }
}
