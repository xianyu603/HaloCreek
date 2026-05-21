# History Session 刷新后选中项丢失问题

## 现象

在 History Sessions 列表中选中最新 session 后，如果该 session 收到新的 agent 回复，下一轮历史 session 刷新后列表项仍然存在，但 UI 会取消选中，底部详情也随之清空。

该问题更容易出现在最新 session 上，因为最新 session 的 JSONL 文件仍在持续追加，`LastUpdatedAt`、`LastReply` 等展示字段会变化。

## 初步判断

目前不认为是 Codex session id 变化导致。

`CodexSessionHistoryReader` 读取 `session_meta.payload.id` 作为 `HistorySessionInfo.Id`。agent 回复只会追加新的 event record，不应该改写已有的 `session_meta` id。

更可能的链路是：

1. `HistorySessionsViewModel.ApplySearch()` 先替换 `Sessions`。
2. `ListBox.SelectedItem` 与 `SelectedSession` 是 TwoWay binding。
3. 刷新后同一个 session 会被重新构造成新的 `HistorySessionInfo` record。
4. 如果 `LastUpdatedAt`、`LastReply` 等字段变化，旧 selected item 不再等于新列表中的 item。
5. Avalonia `ListBox` 在 ItemsSource 替换时发现旧 selected item 不存在，于是将 `SelectedItem` 清空。
6. TwoWay binding 把清空状态回写到 `HistorySessionsViewModel.SelectedSession`。
7. `ApplySearch()` 后续再按 id 匹配时，`SelectedSession` 已经是 `null`，无法恢复选中项。

## 相关位置

- `HaloCreek/ViewModels/Tabs/HistorySessionsViewModel.cs`
  - `ApplySearch()` 当前先设置 `Sessions`，再读取 `SelectedSession.Id` 做重选。
- `HaloCreek/Views/Tabs/HistorySessionsView.axaml`
  - `ListBox.SelectedItem` 以 TwoWay binding 绑定到 `SelectedSession`。
- `HaloCreek/Services/SessionHistory/CodexSessionHistoryReader.cs`
  - `HistorySessionInfo.Id` 来自 `session_meta.payload.id`。

## 后续修复方向

刷新或搜索应用前先缓存当前 `SelectedSession?.Id`，替换 `Sessions` 后用缓存的 id 重新选择。

更稳的方案是让 ViewModel 单独维护一个业务层面的 `selectedSessionId`，避免控件在 ItemsSource 替换过程中的临时清空直接丢失业务选中状态。

同时可以补一个测试覆盖：

1. 初始列表包含 session A。
2. 选中 session A。
3. 刷新列表中仍包含同 id 的 session A，但 `LastUpdatedAt` 或 `LastReply` 已变化。
4. 验证刷新后 `SelectedSession.Id` 仍为 A。
