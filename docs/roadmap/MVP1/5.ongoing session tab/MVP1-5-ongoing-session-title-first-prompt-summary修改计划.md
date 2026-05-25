# MVP1-5 OngoingSessions Title 改为 First Prompt Summary 修改计划

- 目标：`OngoingSessionInfo.Title` 不再固定为 `Codex session` / `Codex resume session`，改为 first prompt summary。
- 数据来源：
  - `Launch`：使用 `SessionLifecycleService.Launch(..., promptText, ...)` 里的 `promptText`。
  - `Resume`：使用 `HistorySessionInfo.InitialPrompt`。
- 标题生成：新增一个很小的 `BuildFirstPromptSummary(string text)`，只做 `Trim`、去空行/压缩空白、固定长度截断，截断长度写死到代码里。
- Fallback：summary 为空时，`Launch` 仍用 `Codex session`，`Resume` 仍用 `Codex resume session`。
- 代码改动：
  - `SessionLifecycleService.Launch` 先算 `title`，再传给 `TmuxLaunchRequest` 和 `OngoingSessionInfo`。
  - `SessionLifecycleService.Resume` 先从 `session.InitialPrompt` 算 `title`，再传给 `TmuxLaunchRequest` 和 `OngoingSessionInfo`。
- UI：`PromptEditorView.axaml` 继续绑定 `Title`，不改布局。
- tmux metadata：`@halocreek.title` 自动跟随新的 `TmuxLaunchRequest.Title`，不另加字段。
- 不做：不调用 LLM、不异步生成 summary、不持久化到配置或缓存、不迁移已存在的 ongoing session。
- 验证：
  - Prompt Editor 新 Launch 后，ongoing 列表标题显示 prompt 摘要。
  - History Resume 后，ongoing 列表标题显示历史 first prompt 摘要。
  - 长 prompt 被截断，空 prompt 走 fallback。