# MVP1-4 T06：History Session 定时刷新补充决策

## 1. 背景

原执行说明中 `4-T06` 只确认了“workspace 不变时历史列表自动更新”，但没有明确刷新调度逻辑放在哪一层。

当前实现中：

- `CodexSessionHistoryReader` 负责读取 Codex JSONL 文件并映射为稳定模型。
- `SessionHistoryService` 负责加载配置、调用 reader、按 `LastUpdatedAt` 倒序排序。
- `HistorySessionsViewModel` 负责 workspace 切换后触发一次加载、搜索过滤、选中项和展示状态。

如果把定时刷新、并发防抖、workspace 切换时的旧结果丢弃、错误保留旧列表等逻辑直接放进 ViewModel，会导致 ViewModel 变臃肿。若直接塞进现有 `SessionHistoryService`，也会把一次性查询服务变成有生命周期的后台刷新协调器。

## 2. 采用方案

采用两个服务边界：

```text
HistorySessionsViewModel
└─ SessionHistoryRefreshService
   └─ SessionHistoryQueryService
      └─ ISessionHistoryReader
         └─ CodexSessionHistoryReader
```

执行 `4-T06` 时，将现有 `SessionHistoryService` 更名为 `SessionHistoryQueryService`，并新增 `SessionHistoryRefreshService`。

暂不引入通用 `RefreshCoordinator`。当前只有 History Sessions 需要此刷新能力，过早抽象会把不同页面未来可能不同的刷新语义提前绑定到一起。等 Git、Ongoing Sessions 或其他页面出现第二个相同模式后，再考虑抽通用协调器。

## 3. 职责划分

### 3.1 `CodexSessionHistoryReader`

职责保持不变：

- 定位 `$CODEX_HOME/sessions` / `~/.codex/sessions`。
- 枚举并解析 Codex JSONL 文件。
- 处理 Windows / WSL workspace 路径等价。
- 将不稳定的 Codex 文件格式转换为稳定的 `HistorySessionInfo`。
- 单个坏文件跳过，不让整个列表不可用。

`CodexSessionHistoryReader` 是唯一知道 Codex JSONL 结构的模块。

### 3.2 `SessionHistoryQueryService`

从现有 `SessionHistoryService` 更名而来，定位为 History Session 的一次性查询用例层。

当前职责：

- 根据 workspace 加载有效配置。
- 调用 `ISessionHistoryReader.ReadSessions(...)`。
- 对结果按 `LastUpdatedAt` 倒序排序。
- 返回 `SessionHistoryResult`。

未来可以承接但 T06 不需要实现的职责：

- 查询级缓存或轻量增量 diff。
- 多 history 来源聚合。
- 搜索策略从 ViewModel 下沉后的查询过滤。
- session 可用性校验。

该服务不负责 timer、不持有 workspace 生命周期状态、不直接操作 UI 状态。

### 3.3 `SessionHistoryRefreshService`

新增服务，定位为 History Session 自动刷新协调器。

职责：

- 保存当前 workspace path。
- 接收 `HistorySessionsViewModel` 传入的刷新完成回调。
- workspace 变化时立即调度一次异步加载。
- 在 workspace 不变时按固定间隔异步刷新。
- 防止并发刷新重入：上一次刷新未完成时，本次 tick 跳过。
- workspace 切换后，旧 workspace 的迟到结果不得覆盖新 workspace 的列表。
- 刷新成功或失败后，通过回调把结果通知给 ViewModel。

`SessionHistoryRefreshService` 不直接持有 `HistorySessionsViewModel` 类型，也不主动修改 VM 属性。它只负责调度刷新并触发回调；是否替换 `_loadedSessions`、是否更新 `Sessions`、是否更新 Footer 状态，都由 ViewModel 在回调中决定。

建议回调传递一个明确结果对象，而不是只传 `SessionHistoryResult`：

```csharp
public sealed record SessionHistoryRefreshResult(
    string? WorkspacePath,
    SessionHistoryResult? HistoryResult,
    string? ErrorMessage);
```

其中：

- `HistoryResult` 非空表示刷新成功。
- `ErrorMessage` 非空表示刷新失败。
- `WorkspacePath` 用于 ViewModel 再次校验结果是否仍属于当前 workspace。

该服务可以依赖 Avalonia 的 `DispatcherTimer` 或 .NET timer。若回调不保证在 UI 线程触发，ViewModel 必须在回调中切回 UI 线程后再更新可绑定属性。

### 3.4 `HistorySessionsViewModel`

ViewModel 不再直接引用 `SessionHistoryQueryService`。

职责：

- 绑定 `SearchText`、`Sessions`、`SelectedSession` 等 UI 状态。
- 对 `_loadedSessions` 做内存搜索过滤。
- 展示加载、空态、跳过文件数量、错误状态。
- 向 `SessionHistoryRefreshService` 注册刷新完成回调。
- 调用 `SessionHistoryRefreshService` 更新 workspace、启动立即刷新和定时刷新。
- 在刷新完成回调中更新 `_loadedSessions`、重新应用搜索、维护选中项和状态文本。

ViewModel 不解析 JSONL、不直接枚举文件、不实现 timer 细节。

## 4. `SetWorkspacePath` 同步入口与内部异步加载

对外仍保留同步 `SetWorkspacePath(...)` 入口，不要求 `MainWindowViewModel.ApplyValidatedWorkspacePath(...)` 改为 async。

T06 起，`HistorySessionsViewModel.SetWorkspacePath(...)` 只做轻量同步工作：

```csharp
public void SetWorkspacePath(string workspacePath)
```

同步部分只允许：

- 保存 `WorkspacePath`。
- 清理或标记当前加载状态。
- 把 workspace 传给 `SessionHistoryRefreshService`。
- 请求 `SessionHistoryRefreshService` 调度立即刷新。

真正的 session 文件扫描和解析必须在内部异步执行，不能在 UI 线程同步完成。

推荐调用关系：

```text
MainWindowViewModel.ApplyValidatedWorkspacePath(...)
└─ HistorySessionsViewModel.SetWorkspacePath(...)
   ├─ SessionHistoryRefreshService.SetWorkspacePath(...)
   └─ SessionHistoryRefreshService.RequestRefresh()
      └─ 后台执行 SessionHistoryQueryService.GetSessions(...)
         └─ 完成后触发 ViewModel 注册的回调
```

`SessionHistoryRefreshService` 内部可以维护刷新状态标记，例如：

- `_isRefreshRunning`：防止并发重入。
- `_refreshVersion` 或 `_workspaceVersion`：识别 workspace 切换后的迟到结果。
- `_currentWorkspacePath`：timer tick 时使用的当前 workspace。

后台刷新必须捕获异常，并通过刷新结果回调返回错误，不能产生未观察异常。

### 4.1 异步完成边界

`SessionHistoryRefreshService` 不允许在 `SetWorkspacePath(...)` 或 `RequestRefresh(...)` 的同一调用栈内触发刷新完成回调。

即使刷新结果可以立即得到，例如 workspace 为空、配置读取失败、reader 很快返回、T06b 暂时只做占位实现，也必须跨过一个异步边界后再通知 ViewModel。这样可以避免请求刷新时同步成功回调重入 VM，导致调用方在同一栈内读到已经被回调改写过的状态。

实现时可以采用：

- 后台扫描使用 `Task.Run(...)`。
- 回调统一通过 UI dispatcher 的 `Post(...)` 触发，而不是直接调用。
- 如果使用 `TaskCompletionSource`，应使用 `TaskCreationOptions.RunContinuationsAsynchronously`。
- `RequestRefresh()` 只表示“已接受或已忽略刷新请求”，不表示“刷新已完成”。

## 5. 刷新行为规则

- workspace 为空时停止刷新，并将已加载列表置空。
- workspace 切换时立即刷新一次，不等待下一个 timer 周期。
- timer 周期只负责重新扫描当前 workspace。
- 搜索框变更只过滤当前 `_loadedSessions`，不触发重新扫描。
- 刷新成功回调中的新列表由 ViewModel 替换 `_loadedSessions` 后，再应用当前 `SearchText`。
- 当前选中 session 如果在刷新后的过滤结果中不存在，则清空选中项。
- 单次刷新跳过文件数量大于 0 时，可继续使用 Footer 状态提示。
- 刷新失败回调中，ViewModel 保留旧 `_loadedSessions` 和当前筛选结果，并在 Footer 显示错误。
- `SessionHistoryRefreshService` 不直接修改 `Sessions`、`SelectedSession` 或 Footer 状态。

## 6. T06 实施建议

建议拆成三条连续工作项执行，先完成命名和接线边界，再补齐真正的刷新行为，避免一次提交同时混入重命名、依赖调整和 timer 细节。

- [x] **4-T06a 旧类改名**：将 `SessionHistoryService` 更名为 `SessionHistoryQueryService`，同步文件名、类名、构造函数、默认 VM 构造和 `App.axaml.cs` 中的创建逻辑；本任务不改变行为，只保持现有“加载配置、调用 reader、按 `LastUpdatedAt` 倒序排序、返回 `SessionHistoryResult`”能力。验收点是改名后项目可构建，History Sessions 在 workspace 切换时仍保持现有一次性加载行为。
- [ ] **4-T06b 新类和接线调整**：新增 `SessionHistoryRefreshService` 和必要的刷新结果模型，将 `HistorySessionsViewModel` 的依赖从 `SessionHistoryQueryService` 调整为 `SessionHistoryRefreshService`，并由 VM 向 refresh service 注册刷新完成回调；本任务允许 `SessionHistoryRefreshService` 暂时为空实现或只做异步排队占位，不要求 timer、重入防护、workspace 版本校验和失败保留旧数据全部可用，但不得在 `SetWorkspacePath(...)` 或 `RequestRefresh(...)` 的同一调用栈内触发刷新完成回调。验收点是依赖方向已调整为 `HistorySessionsViewModel -> SessionHistoryRefreshService -> SessionHistoryQueryService`，refresh service 不直接修改 VM 属性，项目可构建；此阶段 History Sessions 功能允许暂时不可用或只保持最小可用。
- [ ] **4-T06c 新类实现**：在 `SessionHistoryRefreshService` 内补齐 workspace 切换后的立即异步刷新、固定间隔刷新、重入防护、workspace 版本校验、异常捕获和结果回调；`HistorySessionsViewModel.SetWorkspacePath(...)` 对外保持同步入口，内部只调度异步刷新，并在刷新回调中更新 `_loadedSessions`、重新应用当前 `SearchText`、维护选中项和 Footer 状态。验收点是不切换 workspace 仅等待定时周期也能发现新增或变化的 session 文件，搜索不触发重新扫描，刷新失败不清空当前可见列表，workspace 快速切换时旧结果不会覆盖新列表。

## 7. 验收补充

- `HistorySessionsViewModel` 不再直接引用 `SessionHistoryQueryService`。
- `SessionHistoryRefreshService` 不直接修改 `HistorySessionsViewModel` 属性，只通过回调通知刷新结果。
- `HistorySessionsViewModel.SetWorkspacePath(...)` 保持同步入口。
- workspace 切换后的立即加载由 ViewModel 内部调度为异步刷新，不会同步阻塞 UI 线程。
- 不切换 workspace，仅等待定时周期也能发现新增或变化的 session 文件。
- 搜索不会触发重新扫描文件。
- 刷新失败不会清空当前可见列表。
- workspace 快速切换时，旧 workspace 的迟到刷新结果不会覆盖新 workspace。
