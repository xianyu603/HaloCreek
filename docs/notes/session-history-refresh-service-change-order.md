# SessionHistoryRefreshService 调整顺序

## 1. 显式状态机

目标：先把刷新调度模型稳定下来，替换零散 boolean 状态。

要做：
- 用 `Idle / Scheduled / Refreshing / Disposed` 表达刷新服务状态。
- 明确 `SetWorkspacePath`、timer tick、刷新完成、Dispose 的状态迁移。
- workspace 切换时记录版本号或等价标记，旧刷新结果不得覆盖新 workspace。
- 保持服务只负责调度和回调，不直接修改 ViewModel。

## 2. 历史文件缓存

目标：避免每轮刷新都完整读取未变化的 JSONL 文件，并让切 workspace 复用已加载数据。

要做：
- reader 内维护文件缓存，按文件路径记录元数据和解析结果。
- 文件未变化时复用缓存结果，不再逐行解析。
- 文件变化判断优先使用 `LastWriteTimeUtc + Length`，暂不默认 checksum。
- 缓存保留所有已解析 workspace 的 session，查询时按当前 workspace 过滤。
- 继续受 `MaxSessionHistoryFiles` 限制；缓存范围是枚举范围内的历史文件。

## 3. UI 变化检测

目标：只有当前 UI 可见内容变化时才刷新绑定，避免打断详情区复制。

要做：
- 刷新成功但可见列表等价时，不替换 `Sessions`。
- 选中 session 内容未变化时，不重置 `SelectedSession` 和详情文本。
- 仅在增删 session、排序变化、可见字段变化、选中详情变化时通知 UI。
- 搜索仍只过滤已加载数据，不触发文件扫描。

## 推荐顺序

1. 先改 `SessionHistoryRefreshService` 状态机。
2. 再改 `CodexSessionHistoryReader` 文件缓存和全 workspace 内存复用。
3. 最后改 `HistorySessionsViewModel` 的可见内容 diff。
