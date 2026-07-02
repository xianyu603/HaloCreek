# Session State 数据源接入方案

## 目标

实现一个按 Codex session id 读取的事实型数据源：解析目标 session 的 JSONL 文件，产出 `SessionStateSnapshot`，供上层 service / VM 判断展示和通知策略。

## 模型

`SessionStateSnapshot : IKeyedWorkspaceSnapshot<SessionStateSnapshot>`

- `State`：`TaskStarted` / `TaskAborted` / `TaskCompleted` / `Unknown`。
- `StateTimestamp`：最后一个状态事件的 timestamp，用于区分不同轮次的完成/中断。
- `Messages`：按文件顺序收集 `event_msg` 中的 `user_message` 和 `agent_message`。
- `TokenInfo`：最后一条 `token_count` 的事实数据。

辅助模型：

- `SessionMessage(TimeStamp, Sender, Message, Phase)`，`Sender = User / Agent`。
- `SessionTokenInfo(ContextWindow, TotalTokenUsageTotal, LastTokenUsageTotal)`。

## 技术决策

1. session id 使用 `WorkspaceSnapshotStore.Create<SessionStateSnapshot>(sessionId)` 传入，只支持 keyed 读取；`IWorkspaceSnapshot` 的无参 `ReadSnapshot()` 在该类型中显式抛 `NotSupportedException`，避免误用。
2. session 文件按文件名匹配 session id，沿用调研结论，不再用 `session_meta.payload.id` 做二次印证。
3. JSONL 文件格式只由新的 reader 负责，建议命名为 `CodexSessionStateReader`；Snapshot 只负责把 reader 结果包装成 store 可发布的 snapshot。
4. Codex sessions root 定位和按文件名查 session 文件的逻辑抽成内部 helper 复用, 写成一个公共Infrastructure；JSON 字段解析仍分别留在 history/state reader 内。
5. 数据源只收集事实，不判断“是否需要通知”“是否完成当前任务”“是否可继续追问”。这些判断放在上层 service / VM。
6. 状态事件只认 `event_msg.payload.type`：
   - `task_started` => `TaskStarted`
   - `turn_aborted` => `TaskAborted`
   - `task_completed` => `TaskCompleted`
   - 都没有 => `Unknown`

   取文件中最后一个状态事件作为当前事实。
7. 消息只认 `event_msg.payload.type = user_message / agent_message`，文本字段缺失或为空时跳过该条消息；`agent_message.payload.phase` 原样映射到 enum，未知值映射为 `Unknown`。
8. Token 只取最后一条 `event_msg.payload.type = token_count` 的 `payload.info`；只读取 `context_window`、`total_token_usage.total`、`last_token_usage.total`，缺字段则该字段留空，不猜替代字段。
9. session 文件不存在视为正常业务空态，返回 `CreateEmpty()`；JSON 非法、文件 IO 错误、关键结构类型错误则抛出，由 `WorkspaceSnapshotStore` 记录 refresh failed 并保留上一份快照。

## 接入步骤

1. 新增 `SessionStateSnapshot` 和辅助 record/enum。
2. 新增 `CodexSessionStateReader.ReadSessionState(sessionId)`，复用当前 WSL `~/.codex/sessions` 定位策略。
3. 在需要观察单个 session 的 service / VM 中创建 keyed store，并在 session id 变化时释放旧 store、创建新 store。
4. 上层订阅 `Changed`，根据 `State + StateTimestamp + TokenInfo` 做 UI 状态、通知或 context percentage 计算。

## 暂不做

- 不做增量读取缓存。
- 不监听文件系统事件，继续走 `WorkspaceSnapshotStore` 的 10 秒刷新。
- 不把外部 JSON 字段猜测为兼容格式。
- 不改 `IWorkspaceSnapshot` 基础接口。
