# TmuxService 异步风险

1. **Launch / Exit 顺序缺少 per-session 约束**
   `Launch()` 返回只代表拿到 identifier，不代表后台 launch 已完成。`Exit(identifier)` 必须等待同一 session 的 launch 过程结束或进入可判定状态后，再 best-effort kill；否则可能出现 exit 先 no-op、launch 后创建残留 session。Dispose 也需要把 launch 纳入等待边界。

2. **`_frontSessionId` 跨线程读写无同步**
   UI 线程的 `SwitchFrontClient`、后台 `Exit` task、`Dispose` 都会读写 `_frontSessionId`。这里需要明确同步边界，否则 front session 状态可能被旧任务覆盖或清空。

3. **异步任务缺少完整生命周期管理**
   `_exitTasks` completed task 正常运行期间不清理，异常观察依赖 Dispose；同时 launch task 当前没有 tracking。需要让任务完成后自清理，并保证异常被观察。
