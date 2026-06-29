using System;

// 后续演进TODO
// 现在是由业务自己读current workspace, 理论上有 A(发起刷新)  B(业务在这里读到了B的context)  A(切回来 发布了B的赌气结果) 风险. 后续统一由store解决
// 若恢复增量扫描缓存，应由 Store 提供扫描上下文，不放在 Reader 静态状态里。
// 需要更快响应文件变化时，由具体 Snapshot 声明关心的路径，再在 Store 统一接文件系统事件。
// 需要抢占慢速快照读取时，由 Store 统一提供刷新取消能力，不在具体 Reader 内部维护生命周期状态。


namespace HaloCreek.Services.WorkspaceSnapshots
{
    public interface IWorkspaceSnapshot<TSnapshot>
        where TSnapshot : IWorkspaceSnapshot<TSnapshot>
    {
        static abstract TSnapshot CreateEmpty(WorkspaceContext workspace);

        static abstract TSnapshot ReadSnapshot(WorkspaceContext workspace);

        static abstract bool ContentEquals(TSnapshot left, TSnapshot right);
    }

    public interface IWorkspaceSnapshotSource<TSnapshot>
    {
        TSnapshot Current { get; }

        bool HasSuccessfulRefresh { get; }

        event EventHandler? Changed;

        void RequestRefresh(SnapshotRefreshReason reason);
    }

    public enum SnapshotRefreshReason
    {
        WorkspaceChanged,
        Timer,
        Manual,
        FileSystemChanged,
    }
}
