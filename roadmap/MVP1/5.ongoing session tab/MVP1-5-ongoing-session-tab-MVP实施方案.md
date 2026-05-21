# MVP1 阶段 5：Ongoing Session Tab MVP 实施方案

## 1. 目标

阶段 5 先实现一个可验证的最小闭环：

- 从 Prompt Editor 启动的 Codex session 进入 WSL tmux 后台运行。
- 从 History Sessions `Resume` 启动的 Codex session 也进入 WSL tmux 后台运行，并纳入 ongoing 管理。
- Ongoing Sessions 页签能展示 HaloCreek 当前维护的 ongoing session。
- 点击 `Bring to Front` 后，能够打开或复用 Windows Terminal，并 attach 到对应 tmux session。
- 点击 `Exit` 后，能够结束对应 tmux session，并从 ongoing 列表移除。
- 应用退出时清理 HaloCreek 当前维护的 tmux session。

本 MVP 的价值判断是：先证明正常用户路径可以跑通，再补更完整的外部状态探测、错误可感知和恢复能力。

## 2. MVP 原则与非承诺

### 2.1 允许弱一致

MVP 不做强一致承诺。内部状态允许和外部真实状态存在偏差，只要求在 HaloCreek 提供的正常操作路径内结果正确：

- 用户从 HaloCreek 启动 session。
- 用户从 HaloCreek 将 session 置于前台。
- 用户从 HaloCreek 退出 session。
- 用户正常关闭 HaloCreek，HaloCreek 执行 cleanup。

以下外部行为在 MVP 中不保证结果正确：

- 用户手动关闭 Windows Terminal 前台窗口。
- 用户手动 detach、kill 或 rename tmux session。
- 用户在应用退出后手动启动或保留 HaloCreek 旧 tmux session。
- WSL、tmux、Windows Terminal 异常退出。

这些行为后续再通过错误可感知、外部状态探测和启动恢复补齐。

### 2.2 应用启动与退出假设

- 应用启动时假设没有 ongoing session，不扫描和接管孤儿 tmux session。
- 应用运行期间只维护本进程创建并记录的 session。
- 应用退出时 best effort 释放当前内部记录的所有 tmux session。
- 如果 cleanup 失败，MVP 不要求下次启动自动修复。

### 2.3 状态语义是内部模型

Ongoing session 状态表示 HaloCreek 当前内部模型，不承诺是外部世界的实时真相。

状态变化可以有延迟，也不要求最终一定和外部状态同步。只要用户沿 HaloCreek 正常路径操作，内部模型需要保持正确。

### 2.4 技术限制

能力验证中发现，使用 tmux control mode 监听 session 状态会导致对应 tmux session 的前台窗口行为异常。为快速完成 MVP，本阶段约束 `Front` 和 watch 不共存：

- `Front` session 不进入 TmuxService 状态监听。
- 被 TmuxService watch 的 session 必须是后台 session。
- session 被 `BringToFront` 时，SessionLifecycleService 先停止该 session 的 watch，再拉起前台终端，最后把内部状态置为 `Front`。
- 原 `Front` session 被切回后台时，SessionLifecycleService 先把内部状态改为后台状态，再恢复该 session 的 watch。

## 3. 模块边界

### 3.1 SessionLifecycleService

SessionLifecycleService 是 UI 面向的 session 编排层，聚合 tmux 后台运行能力、Windows Terminal 前台呈现能力，以及 Prompt Editor Launch / History Sessions Resume 对 session 生命周期的统一入口。

当前代码中已有 `SessionLifecycleService`，阶段 5 沿用该命名和现有注入关系。内部可以按本方案拆出 `TmuxService` 和 `TerminalService`，模块边界以本阶段文档为准。

职责：

- 提供 `Launch`、`Resume`、`BringToFront`、`Exit`、`GetOngoingSessions`。
- 维护 HaloCreek 内部 session 表。
- 维护哪个 session 当前是 `Front`。
- 在 session 被置为 `Front` 时，停止该 session 的 tmux 状态监听。
- 在 session 变为非 `Front` 时，恢复该 session 的 tmux 状态监听。
- 接收 TmuxService 对非前台 session 的状态通知，并更新内部 session 表。
- 应用退出时调用 cleanup。

不负责：

- 直接拼接底层 tmux 命令。
- 直接启动 Windows Terminal 进程。
- 扫描或接管用户已有 tmux session。
- 保证用户手动破坏外部状态后的正确性。

建议接口：

```csharp
public sealed class SessionLifecycleService
{
    public SessionLaunchResult Launch(string workspacePath, string promptText, AppConfig config);
    public SessionResumeResult Resume(HistorySessionInfo session, string currentWorkspacePath, AppConfig config);
    public IReadOnlyList<OngoingSessionInfo> GetOngoingSessions(string? workspacePath);
    public void BringToFront(string sessionId);
    public void Exit(string sessionId);
    public void Cleanup();

    public event EventHandler? SessionsChanged;
}
```

MVP 中 `BringToFront`、`Exit` 可以是 best effort。后续需要错误可感知时，再改为返回 operation result。

### 3.2 TmuxService

TmuxService 只负责 HaloCreek 创建的 tmux session 生命周期和非前台状态监听。

职责：

- 拉起 tmux session，并返回稳定 identifier。
- 退出指定 tmux session。
- 为指定 tmux session 生成前台 attach/switch 指令，供 TerminalService 使用。
- 对非 `Front` session 开始状态监听。
- 对非 `Front` session 取消状态监听。
- 通知 identifier 持有者：该 tmux session 的内部探测状态发生变化。

不负责：

- 判断哪个 session 是 `Front`。
- 直接拉起 Windows Terminal。
- 探测 `Front` session 的 activity。
- 管理 UI 展示模型。

建议接口：

```csharp
public sealed class TmuxService
{
    public string Launch(TmuxLaunchRequest request);
    public void Exit(string identifier);
    public TerminalCommandSpec GetFrontCommand(string identifier);

    public void StartWatching(string identifier);
    public void StopWatching(string identifier);

    public event EventHandler<TmuxSessionStateChangedEventArgs>? StateChanged;
}
```

`StartWatching` 和 `StopWatching` 的存在是为了让 SessionLifecycleService 实现 `Front` 和 watch 的互斥：

- session 进入 `Front`：SessionLifecycleService 先调用 `StopWatching(identifier)`，再拉起前台终端，最后将内部状态设为 `Front`。
- session 离开 `Front`：SessionLifecycleService 先将内部状态改为后台状态，再调用 `StartWatching(identifier)`。

TmuxService 发出的状态只适用于被 watch 的后台 session。它不产生 `Front` 状态。

### 3.3 TerminalService

TerminalService 负责启动或复用 Windows Terminal，并执行 TmuxService 返回的前台指令。

职责：

- 拉起或重新拉起前台 Windows Terminal。
- 用 TmuxService 返回的 attach/switch 指令启动一个前台终端。
- 尽量让用户能看到并操作目标 tmux session。

不负责：

- 判断 session 状态。
- 维护 session 列表。
- 直接创建或退出 tmux session。

建议接口：

```csharp
public sealed class TerminalService
{
    public void LaunchOrRelaunch(TerminalCommandSpec command);
}
```

`LaunchOrRelaunch` 的语义是用新的前台指令启动 Windows Terminal。能力验证中发现，合适的 `wt.exe` 参数可以表现为新建 WT 或拉起已有 WT。本阶段只管理至多一个 HaloCreek 前台 WT 入口，不做 Win32 窗口查找、命令注入、多 WT 窗口枚举或强复用保证。

`TerminalCommandSpec` 可以先保持简单，但应表达为结构化命令，而不是 UI 层拼接的裸字符串：

```csharp
public sealed record TerminalCommandSpec(
    string WorkingDirectory,
    string Executable,
    IReadOnlyList<string> Arguments);
```

## 4. 状态模型

MVP 使用互斥状态：

```text
Front
BackgroundRunning
BackgroundIdle
Unknown
```

语义：

- `Front`：SessionLifecycleService 内部认为该 session 当前被前台 Windows Terminal attach。该状态由 SessionLifecycleService 维护，不来自 TmuxService probe。
- `BackgroundRunning`：该 session 是后台 session，TmuxService 监听到近期有输出或 activity。
- `BackgroundIdle`：该 session 是后台 session，TmuxService 监听到近期没有输出或 activity。
- `Unknown`：后台探测失败、tmux 输出缺字段或解析失败。

`Exit` 不是状态。session 退出后从内部表移除。

当前代码里的 `OngoingSessionState.Starting/Running/WaitingForInput/Exited` 是早期占位模型，尚未被真实 ongoing 管理使用。阶段 5 以本状态模型为准，不保留旧占位状态，也不存在需要兼容的迁移风险。

`BackgroundIdle` 不等同于 Codex 一定在等待用户输入，只表示后台 session 最近没有被监听到输出。MVP 不解析 Codex 内部运行阶段。

## 5. Front 与 Watch 互斥

之前能力验证表明：稳定探测 tmux 活动状态需要 control mode，而 control mode 会影响前台交互窗口行为。因此 MVP 采用以下约束：

- `Front` session 不做 tmux activity probe。
- TmuxService 只 probe 非 `Front` session。
- `Front` 状态由 SessionLifecycleService 根据 HaloCreek 内部操作路径维护。
- 当某 session 被 `BringToFront`，SessionLifecycleService 停止监听该 session。
- 如果另一个 session 被 `BringToFront`，原 front session 变为后台 session，并重新开始监听。

用户手动关闭前台 WT、手动 detach 或手动切换 tmux client 后，MVP 不保证 `Front` 状态立即正确。

## 6. Session Identifier

MVP 只维护 HaloCreek 创建的 tmux session，不扫描或接管用户已有 tmux session。

建议 session name：

```text
halocreek-{workspaceHash}-{shortId}
```

`OngoingSessionInfo.Id` 使用该 tmux session name，不使用 Windows Terminal process id。

可选 tmux user option：

```text
@halocreek.workspace=/mnt/d/work/HaloCreek
@halocreek.startedAt=2026-05-21T...
@halocreek.title=Codex session
```

MVP 不依赖启动时扫描这些 metadata。metadata 主要用于调试和后续恢复能力。

## 7. 正常路径流程

### 7.1 Launch

Prompt Editor 调用 SessionLifecycleService：

```text
SessionLifecycleService.Launch
  -> TmuxService.Launch
  -> 内部 session 表新增 BackgroundRunning 或 BackgroundIdle
  -> TmuxService.StartWatching(identifier)
  -> SessionsChanged
```

tmux 启动命令形态：

```text
tmux new-session -d -s <sessionName> -c <workspace> -- codex <configured args> <prompt>
```

MVP 不处理 prompt 过长导致 shell 参数长度风险。若真实使用触发，再改为临时文件或 stdin。

### 7.2 Resume

History Sessions 调用 SessionLifecycleService：

```text
SessionLifecycleService.Resume
  -> TmuxService.Launch
  -> 内部 session 表新增 BackgroundRunning 或 BackgroundIdle
  -> TmuxService.StartWatching(identifier)
  -> SessionsChanged
```

tmux 启动命令形态：

```text
tmux new-session -d -s <sessionName> -c <workspace> -- codex <configured args> resume <sessionId>
```

Resume 后的进程纳入 Ongoing Sessions。本阶段不把应用启动前已经存在的 resume/codex/tmux 进程反向接管进来。

### 7.3 Bring to Front

Ongoing Sessions 调用 SessionLifecycleService：

```text
SessionLifecycleService.BringToFront(sessionId)
  -> 如果已有 front session 且不是目标 session：
       将旧 front session 改为后台状态
       TmuxService.StartWatching(oldIdentifier)
  -> TmuxService.StopWatching(targetIdentifier)
  -> TmuxService.GetFrontCommand(targetIdentifier)
  -> TerminalService.LaunchOrRelaunch(command)
  -> 将目标 session 内部状态设为 Front
  -> SessionsChanged
```

如果前台终端拉起失败，MVP 不做失败回滚设计。本阶段先验证正常路径正确性，后续再补 operation result 和错误可感知恢复。

MVP 不做 Win32 窗口句柄查找、强制抢焦点、多 WT 窗口枚举或跨终端强复用保证。WT 是否新建窗口或拉起已有窗口由 `wt.exe` 参数和系统当前状态决定。

### 7.4 Background 状态监听

TmuxService 对 watch 中的 session 做低成本轮询或事件封装。

状态通知形态：

```text
TmuxService.StateChanged(identifier, BackgroundRunning/BackgroundIdle/Unknown)
  -> SessionLifecycleService 确认该 identifier 当前不是 Front
  -> 更新内部 session 表
  -> SessionsChanged
```

如果通知到达时该 session 已经是 `Front`，SessionLifecycleService 忽略该通知。

### 7.5 Exit

Ongoing Sessions 调用 SessionLifecycleService：

```text
SessionLifecycleService.Exit(sessionId)
  -> TmuxService.StopWatching(identifier)
  -> TmuxService.Exit(identifier)
  -> 从内部 session 表移除
  -> 如果该 session 是 front，清空 front 记录
  -> SessionsChanged
```

如果 tmux session 已不存在，MVP 可直接从内部表移除，不要求向用户解释具体原因。

### 7.6 Cleanup

应用退出时：

```text
SessionLifecycleService.Cleanup
  -> 对内部 session 表逐个 StopWatching
  -> 对内部 session 表逐个 TmuxService.Exit
  -> 清空内部 session 表
```

cleanup 是 best effort。MVP 不要求可靠处理所有失败。

具体挂到 `IClassicDesktopStyleApplicationLifetime.Exit`、主窗口关闭事件，还是其他生命周期点，属于实现细节，编码时按现有 Avalonia 组合根决策。

## 8. Ongoing Sessions UI

最小正式 UI：

- 顶部 `Refresh` 按钮。
- 列表展示 `Title`、`State`、`StartedAt`、`LastActivityAt`、`WorkspacePath`、`Id`。
- 提供 `Bring to Front` 和 `Exit` 操作。
- 空状态显示当前 workspace 没有 ongoing session。

`Refresh` 只刷新 SessionLifecycleService 内部列表，不承诺重新扫描外部 tmux 世界。

## 9. 验证步骤

### 9.1 验证后台运行

1. 在 Prompt Editor 输入一个会保持交互的 prompt 并 Launch，或在 History Sessions 点击 `Resume`。
2. 切到 Ongoing Sessions。
3. 列表能看到该 session。
4. 状态为 `BackgroundRunning`、`BackgroundIdle` 或短暂 `Unknown`。

### 9.2 验证置为前台

1. 在 Ongoing Sessions 选中刚才的 session。
2. 点击 `Bring to Front`。
3. Windows Terminal 打开或复用前台窗口。
4. 终端 attach 到对应 tmux session。
5. Ongoing Sessions 中该 session 状态变为 `Front`。

### 9.3 验证 front/watch 互斥

1. 启动两个 session。
2. 将 session A `Bring to Front`。
3. session A 状态为 `Front`，不再由 TmuxService 监听。
4. session B 仍由 TmuxService 监听，状态可在 `BackgroundRunning` 和 `BackgroundIdle` 间变化。
5. 将 session B `Bring to Front`。
6. session A 回到后台并重新开始监听，session B 变为 `Front`。

### 9.4 验证退出

1. 选中一个 ongoing session。
2. 点击 `Exit`。
3. 目标 tmux session 被关闭。
4. 该 session 从列表消失。

辅助验证命令：

```bash
tmux list-sessions
tmux show-options -t <sessionName> -gqv @halocreek.workspace
tmux kill-session -t <sessionName>
```

## 10. 暂不做

- 不扫描或恢复应用启动前已存在的 tmux session。
- 不接管非 HaloCreek 创建的 tmux session。
- 不保证用户手动关闭 WT、手动 detach、手动 kill tmux 后的状态正确。
- 不实现 Win32 级别窗口查找和抢焦点。
- 不做多窗口、多 pane 管理。
- 不把 ongoing session 写入配置文件。
- 不解析 Codex 内部状态，例如等待输入、工具调用中、模型响应中。
- 不把 operation result 做完整错误分类；MVP 可先 best effort。
