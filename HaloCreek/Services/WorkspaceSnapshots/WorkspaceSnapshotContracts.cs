using System;
using HaloCreek.Services;

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
