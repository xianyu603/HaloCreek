# MVP2 版本总结

## 版本定位

MVP2 的目标是：支持用户对 AI 修改进行人工审阅，并在审阅过程中保留一个轻量的继续沟通入口。

这个版本由两个核心能力组成：

1. 用 Review 面板管理文件级审阅进度。
2. 在 Review 面板中快速向当前或新 Codex session 发送 prompt。

## 最终范围

### Review 面板

Review tab 是 MVP2 的审阅主界面。原 Git tab 的文件列表和文件动作能力已融入 `Modified` 面板，不再作为独立 tab 出现。

左侧用于文件审阅：

- `Modified`：显示当前 Git 修改，复用 Git file browser 的文件动作和双击行为。
- `Unreviewed`：显示 working tree 相对 reviewed 快照的新修改。
- `Reviewed`：显示已标记 reviewed、但尚未进入 HEAD 的内容。

右侧用于审阅沟通：

- 显示剪贴板定位状态。
- 显示 front session 状态。
- 提供常驻 prompt 文本框。
- 支持发送到当前 front session。
- 支持用当前 prompt 拉起新 session。
- 支持将 front terminal 窗口置于前台。

### Reviewed 语义

Reviewed 是文件级审阅快照，不是 Git staging area。

核心规则：

- 未显式标记 reviewed 的文件，reviewed 内容等于 `HEAD:path`。
- 标记 reviewed 后，reviewed 内容等于当时的 working tree 文件内容。
- 后续 AI 或用户继续修改同一文件时，该文件重新出现在 `Unreviewed`。
- `Reviewed` 视图展示 reviewed 快照相对 HEAD 的差异。
- refresh 时清理已经不再有 Git 修改、或 reviewed 内容已经等于 HEAD 的 reviewed 状态。

实现上 reviewed 状态由 HaloCreek 管理的独立 index 文件承载，不写入真实 Git index。

### Diff 和回退

MVP2 不内置 diff viewer，继续使用外部 diff 工具。

支持的 Review 操作：

- `Diff Against Reviewed`
- `Diff Against Working Tree`
- `Diff Against HEAD`
- `Mark Reviewed`
- `Mark Unreviewed`
- `All Reviewed`
- `All Unreviewed`
- `Revert to Reviewed`
- `Revert All to Reviewed`
- `Refresh`

`Revert to Reviewed` 只表达“丢弃 working tree 相对 reviewed 的新修改”。它不负责 prompt 复用、重新发起修改、session 管理或其他完整重开流程。

## 版本边界

### 批注

MVP2 不包含批注功能。

当前版本只保留即时 prompt 入口，不维护批注列表、批注定位、批注失效状态、批量导出或批注清理语义。

### 自动定位注入

MVP2 保留剪贴板定位状态展示，但不在 prompt 文本框中自动注入定位信息。

后续如果要做定位注入，更合适的形态是 Prompt Editor 或文件列表的右键 action。这个方向属于下一阶段的 prompt/action 体系，不混入 MVP2 收尾。

### 放弃当前修改

MVP2 不新增独立的“放弃当前修改并重新发起”入口。

当前形态下，审阅相关状态主要由 Git working tree 和 reviewed 快照组成。如果用户需要放弃当前修改，可以使用 Git revert。Git 修改回退后，Review refresh 会清理对应 reviewed 状态。

prompt 复用、微调、重新发起一轮修改等能力，也放到下一阶段处理。

### Unreviewed/Reviewed Open

打开文件、ExploreTo、Edit 等动作通过 `Modified` 面板复用 Git file browser 的配置能力。

因此 MVP2 暂不在 `Unreviewed` / `Reviewed` 列表中重复增加 `Open` 入口。

### ShowTextInputDialogAsync

`ShowTextInputDialogAsync` 目前没有被最终 Review 形态使用，但作为平台层轻量输入弹窗能力暂时保留。

它不是 MVP2 的产品承诺；除非后续确认长期不需要，再单独清理。

## 不支持项

MVP2 明确不支持：

- 批注。
- hunk 级 reviewed。
- 内置 diff viewer。
- 自动区分 AI 修改和用户手动修改。
- 删除、重命名、冲突、copy 文件进入 reviewed 工作流。
- 自动定位注入 prompt。
- prompt 模板系统。
- 文件级以外的“只回退 unreviewed 部分”。
- 多个并行 front session 的精细语义。
- front session 在发送瞬间切换后的目标锁定。

## 后续方向

后续阶段更适合围绕 Prompt Editor 和 action 体系继续做：

- 文件、定位、选择文本到 prompt 的右键 action。
- prompt 模板和快捷话术。
- prompt 复用、微调和重新发起。
- 更明确的 session 目标选择。
- 如果实际使用中仍需要批注，再基于新的 prompt/action 体系重新设计。

## 收束结论

MVP2 不再继续扩范围。

当前版本保留的价值是：

- Review 面板提供文件级审阅进度。
- Reviewed 快照让 AI 后续修改变得可感知。
- 用户可以在同一个面板里查看 Git 修改、审阅差异，并向 session 继续发送 prompt。

剩余想法进入下一阶段，不再作为 MVP2 收尾任务。
