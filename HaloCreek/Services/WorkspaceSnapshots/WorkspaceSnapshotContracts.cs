using System;

// 后续演进TODO
// 若恢复增量扫描缓存，应由 Store 提供扫描上下文，不放在 Reader 静态状态里。
// 需要更快响应文件变化时，由具体 Snapshot 声明关心的路径，再在 Store 统一接文件系统事件。
// 需要抢占慢速快照读取时，由 Store 统一提供刷新取消能力，不在具体 Reader 内部维护生命周期状态。


namespace HaloCreek.Services.WorkspaceSnapshots
{
    public interface IWorkspaceSnapshot<TSnapshot>
        where TSnapshot : IWorkspaceSnapshot<TSnapshot>
    {
        static abstract TSnapshot CreateEmpty();

        static abstract TSnapshot ReadSnapshot();

        static abstract bool ContentEquals(TSnapshot left, TSnapshot right);
    }

    public interface IKeyedWorkspaceSnapshot<TSnapshot> :
        IWorkspaceSnapshot<TSnapshot>
        where TSnapshot : IWorkspaceSnapshot<TSnapshot>
    {
        // Keyed snapshots are expected to be read through WorkspaceSnapshotStore(string key).
        // The inherited parameterless ReadSnapshot contract is not used by the store for keyed snapshots.
        // Snapshots that support both keyed and keyless reads are not modeled yet.
        // If that requirement appears, add an explicit hybrid contract instead of loosening keyed-only semantics.
        static abstract TSnapshot ReadSnapshot(string key);
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
        Init,
        Timer,
        Manual,
        FileSystemChanged,
    }
}
