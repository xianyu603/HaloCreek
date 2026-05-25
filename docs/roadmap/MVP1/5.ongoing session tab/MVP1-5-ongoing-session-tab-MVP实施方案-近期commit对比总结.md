# MVP1-5 Ongoing Session Tab 近期 commit 对比总结

对比对象：

- 原方案：`MVP1-5-ongoing-session-tab-MVP实施方案.md`
- 最近 8 个提交：`bcb1153`、`baba7dd`、`1c5c38e`、`5052046`、`a92e062`、`3336b29`、`b898190`、`dd238a4`
- 对比时间：2026-05-25

本文只记录原实施方案需要补充或修正的内容，不修改原文档。

## 1. 总体结论

最近几个提交已经把原方案中 5-T05 之后的实现推进到更完整的状态：

- `5-T05 TmuxService 最小真实实现` 已完成，原方案中已经在 `dd238a4` 将该项勾选。
- `5-T06 Cleanup 与资源释放接线` 在代码层面已经完成，但原方案仍显示未完成。
- `5-T07 Tmux watch 实现` 在代码层面已经完成主要实现，但“端到端验证”仍未在提交中形成明确记录。
- 实现新增了一个原方案没有明确写出的 `keeper tmux session` 决策，用于避免退出前台 session 时 Windows Terminal 窗口随 tmux attach 结束而关闭。
- `SessionLifecycleService` 的并发模型从锁保护改为 UI 线程亲和，tmux watch 回调统一投递回 Avalonia UI 线程后再修改内部 session 表。
- cleanup 的公开接口形态从 `Cleanup()` 收束为 `IDisposable.Dispose()`，应用退出通过 `AppServices.Dispose()` 串起资源释放。

## 2. 与任务拆分的差异

### 2.1 5-T05 已完成

现状与原方案基本一致：

- `TmuxService.Launch` 已真实执行 `tmux new-session -d -s <identifier> -c <workspace> -- <codex...>`。
- session name 使用 `halocreek-{workspaceHash}-{shortId}`。
- `Exit` 已执行 `tmux kill-session -t <identifier>`。
- `GetFrontClientStartupCommand` 生成 WSL script，在 attach 前写入前台 client 的 tty marker。
- `SwitchFrontClient` 通过 `tmux switch-client -c <tty> -t <identifier>` 切换固定前台 client。
- 已写入 `@halocreek.workspace`、`@halocreek.startedAt`、`@halocreek.title` metadata。
- 已额外启用 `tmux set-option -t <identifier> mouse on`。

原方案无需大改，只需保留 `5-T05` 已完成状态。

### 2.2 5-T06 代码已完成，文档状态应更新

原方案仍写：

```text
- [ ] 5-T06 Cleanup 与资源释放接线
```

当前代码已经完成主要接线：

- `App.axaml.cs` 中新增 `AppServices : IDisposable`。
- `desktop.Exit += (_, _) => appServices.Dispose();` 已接入应用退出生命周期。
- `AppServices.Dispose()` 按顺序释放 `SessionHistoryRefreshService`、`SessionLifecycleService`、`TmuxService`。
- `SessionLifecycleService.Dispose()` 会：
  - 取消订阅 `TmuxService.StateChanged`。
  - 遍历内部 session 表。
  - best effort 调用 `StopWatching(sessionId)`。
  - best effort 调用 `Exit(sessionId)`。
  - 清空内部 session 表和 front 记录。
- `TmuxService.Dispose()` 会：
  - 将 watch 状态置为 `Disposed`。
  - 清空 watched sessions。
  - 停止并释放 timer。
  - best effort kill keeper session。

建议原方案补充：`Cleanup()` 不再作为 public API 保留，阶段 5 实现采用 `IDisposable.Dispose()` 表达 cleanup 语义。

### 2.3 5-T07 watch 主要实现已完成，但验证状态需要单独标注

当前代码已经实现 `StartWatching` / `StopWatching` / `StateChanged`：

- watch 使用 `System.Threading.Timer`。
- 轮询间隔为 2 秒。
- idle 判定阈值为 10 秒。
- 探测方式为 `tmux capture-pane -p -t <identifier> -S -200`。
- 通过 pane 内容 snapshot 是否变化判断：
  - 内容变化：`BackgroundRunning`
  - 10 秒内未变化：仍保持 `BackgroundRunning`
  - 超过 10 秒未变化：`BackgroundIdle`
  - 探测失败：`Unknown`
- watch 内部新增显式状态机：`Idle`、`Scheduled`、`Probing`、`Disposed`。
- `StopWatching` 后，正在进行中的 probe 结果会因为 watched session 已移除而被忽略。
- `SessionLifecycleService` 会忽略 `Front` session 的 tmux 状态回调，保持 front/watch 互斥。

与原方案的差异：

- 原方案描述能力验证中 control mode 会影响前台行为；当前实现没有继续使用 control mode，而是改为 `capture-pane` 轮询。
- 原方案中 `5-T07` 同时包含“实现”和“端到端验证”。代码已经完成实现部分，但提交信息和文档中没有明确记录完整端到端验证结果。

建议原方案将 `5-T07` 拆成：

- `[x] Tmux watch 实现`
- `[ ] 端到端验证记录补齐`

或保留一个任务但说明“实现已完成，验证待补”。

## 3. 新增技术决策

### 3.1 keeper session

提交 `3336b29` 引入 `keeper tmux session`，这是原方案没有明确写出的关键行为。

当前行为：

- `TmuxService` 构造时创建 `_keeperSessionId = halocreek-keeper-{frontClientId}`。
- 构造阶段调用 `EnsureKeeperSession()`，确保 keeper session 存在。
- 当 `Exit` 的目标是当前 front session 时，先 `SwitchFrontClientCore(_keeperSessionId)`，再 kill 目标 session。
- `TmuxService.Dispose()` best effort kill keeper session。

需要补充到原方案的位置：

- `3.2 TmuxService` 职责中增加“维护前台 client 的 keeper session”。
- `7.5 Exit` 流程中增加“如果退出的是 front session，先将固定前台 client 切到 keeper session，再 kill 目标 session”。
- `7.6 Cleanup` 中增加“退出时释放 keeper session”。

用户可感知影响：

- 退出当前前台 session 时，Windows Terminal 窗口不再因为 tmux attach 目标结束而直接关闭。
- keeper session 属于内部实现细节，不应展示在 ongoing sessions 列表中。

### 3.2 SessionLifecycleService UI 线程模型

提交 `1c5c38e` 将 `SessionLifecycleService` 的并发模型明确为 UI 线程亲和：

- 移除了 `_sessionsLock`。
- 所有 public 生命周期操作都调用 `RequireUiThread()`。
- `HandleTmuxStateChanged` 如果不在 UI 线程，会通过 `Dispatcher.UIThread.Post(...)` 投递回 UI 线程。

这与原方案不冲突，但原方案只描述“内部 session 表”，没有说明线程模型。

建议补充：

- `SessionLifecycleService` 的 session 表只允许 UI 线程直接读写。
- `TmuxService.StateChanged` 是跨线程入口，必须回到 UI 线程后再更新状态。

### 3.3 cleanup API 收束为 Dispose

原方案建议接口包含：

```csharp
public void Cleanup();
```

当前实现已经移除 public `Cleanup()`，由 `IDisposable.Dispose()` 承担清理职责。

建议原方案更新接口示例：

```csharp
public sealed class SessionLifecycleService : IDisposable
{
    public SessionLaunchResult Launch(...);
    public SessionResumeResult Resume(...);
    public IReadOnlyList<OngoingSessionInfo> GetOngoingSessions(string? workspacePath);
    public void BringToFront(string sessionId);
    public void Exit(string sessionId);

    public event EventHandler? SessionsChanged;
}
```

并说明应用退出语义由组合根统一 Dispose，而不是 UI 层显式调用 `Cleanup()`。

### 3.4 PlatformInfrastructure 的 WSL/tmux 支撑能力扩展

`PlatformInfrastructure` 为 `TmuxService` 增加了更明确的底层能力：

- `ConvertPathToWsl`
- `BuildWslShellCommand`
- `QuoteWslShellArgument`
- `TryRunWslCommand`

同时 `TryRunWslShellCommand` 改为：

- Windows 下走 `wsl.exe --exec bash -ic <command>`。
- 非 Windows 下走 `bash -ic <command>`。
- 命令失败时将 stdout + stderr 合并为 output，便于上层生成错误信息。

这符合原方案“tmux 命令构造、WSL 路径和参数转义依赖 PlatformInfrastructure 提供明确能力”的方向。

## 4. 流程修正建议

### 4.1 Bring to Front

原方案流程仍基本成立，但当前实现还有两个细节：

- 如果存在旧 front session，会先将旧 front session 状态置为 `Unknown`，再恢复 watch。
- `SwitchFrontClient` 依赖此前启动脚本写出的 tty marker；如果 marker 不存在或读取失败，当前实现会 best effort 返回，不抛错。

### 4.2 Exit

建议将原方案中的 `Exit` 流程补充为：

```text
SessionLifecycleService.Exit(sessionId)
  -> TmuxService.StopWatching(identifier)
  -> TmuxService.Exit(identifier)
       -> 如果 identifier 是当前 front session：
            先 switch-client 到 keeper session
       -> kill-session identifier
  -> 从内部 session 表移除
  -> 如果该 session 是 front，清空 front 记录
  -> SessionsChanged
```

### 4.3 Cleanup / Dispose

建议将原方案中的 cleanup 流程改为：

```text
AppServices.Dispose
  -> SessionHistoryRefreshService.Dispose
  -> SessionLifecycleService.Dispose
       -> 取消 tmux 状态事件订阅
       -> 对内部 session 表逐个 StopWatching
       -> 对内部 session 表逐个 TmuxService.Exit
       -> 清空内部 session 表
  -> TmuxService.Dispose
       -> 停止 watcher timer
       -> 清空 watched sessions
       -> kill keeper session
```

## 5. 与原方案无关或暂不纳入的提交

以下提交内容不建议写回 MVP1-5 原方案主体：

- `5052046` 主要精简 `Log`，并修改 `todo.txt`，与 ongoing session 方案没有直接关系。
- `1c5c38e` 对 `SessionHistoryRefreshService` 增加了历史刷新缓存相关 TODO，只是顺手记录，不属于本阶段 ongoing session 设计。
- `todo.txt` 中新增的 WSL 孤儿、复制行为、历史刷新体验等问题可以后续拆新任务，不建议混入原 MVP1-5 实施方案。

## 6. 建议的文档更新清单

如果后续要更新原方案，建议只做以下修改：

- 将 `5-T06` 标记为完成，说明实际实现为 `IDisposable.Dispose()` 接线。
- 将 `5-T07` 标记为“实现完成，端到端验证待补”，或拆分实现与验证两个任务。
- 在 `TmuxService` 边界中补充 keeper session 职责。
- 将 `Cleanup()` 接口示例替换为 `IDisposable`。
- 将 watch 实现描述从“control mode 探测”修正为“`capture-pane` 轮询 + snapshot diff”。
- 在 `Exit` 流程中补充 front session 退出前先切到 keeper session。
- 在“暂不做”中继续保留不扫描孤儿 session、不接管外部 tmux、不保证用户手动破坏后的正确性。
