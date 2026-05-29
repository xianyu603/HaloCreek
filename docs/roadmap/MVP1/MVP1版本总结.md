# MVP1 版本总结

总结日期：2026-05-29

## 1. 版本定位

MVP1 的目标是验证 HaloCreek 作为个人 Codex shell wrapper 是否能够显著降低人工介入摩擦：选择 workspace、编辑 prompt、启动或恢复 Codex session、查看历史 session、管理正在运行的 session，并对当前 Git 变更做轻量外部工具接入。

按当前代码现状，MVP1 已经从“UI 骨架验证”推进到“可日常试用的最小闭环”：

- workspace 选择、缓存、配置加载和全局 footer 已接通。
- Prompt Editor 能编辑 prompt、拖入路径、用 `Ctrl+Enter` 或按钮启动 Codex。
- Codex launch/resume 已进入 WSL tmux 后台运行，并在 Prompt Editor 内展示 ongoing sessions。
- History Sessions 能读取本机 Codex JSONL 历史、按当前 workspace 过滤、搜索、resume 和 reedit 初始 prompt。
- Git 页签能读取真实 Git status，并通过配置驱动外部文件动作和双击动作。
- 错误和状态展示策略已初步收束：一过性操作结果主要写日志或弹窗，status bar 只保留后台任务和持续性全局状态。

## 2. 当前产品形态

主窗口当前是三页签加 footer：

```text
MainWindow
├─ Prompt Editor
│  ├─ prompt 编辑区
│  ├─ ongoing sessions 横向列表
│  └─ Launch
├─ History Sessions
│  ├─ 搜索
│  ├─ session 列表
│  ├─ 选中 session 的 Initial Prompt / Last Prompt / Last Reply 详情
│  └─ Resume / Reedit
├─ Git
│  ├─ 配置驱动的左侧文件动作
│  ├─ 配置驱动的右侧 root action
│  ├─ Refresh
│  └─ Git 变更列表
└─ Footer
   ├─ 当前 workspace
   ├─ 选择 workspace
   └─ 全局状态文本
```

原计划中的独立 `Ongoing Sessions` tab 已被移除，ongoing session 管理内嵌到 Prompt Editor。这个形态更贴近实际工作流：用户启动 prompt 后立刻在同一页看到后台 session，并能双击或操作按钮拉回前台。

## 3. 主要交付内容

### 3.1 Workspace / Footer / Config

- `WorkspaceRuntimeService` 成为 workspace 切换和有效配置分发的聚合入口。
- 启动时优先使用上次缓存 workspace；无有效缓存时 fallback 到用户 home，再 fallback 到应用目录。
- workspace 必须是已存在目录，切换成功后统一分发给 Prompt、History、Git 和 Footer。
- `ConfigService` 已支持全局配置与 workspace `.HaloCreek/config.json` 分层合并。
- 当前配置项包括 Codex executable、Codex launch arguments、history 最大解析文件数、diff tool 旧字段、Git File Browser 双击 action id 和动作列表。

### 3.2 Prompt Editor

- 支持多行 prompt 编辑。
- 支持拖入文件或目录，把第一个路径追加到 prompt 文本。
- `Launch` 调用 `SessionLifecycleService.Launch(...)`，通过 `TmuxService` 在 WSL tmux 中后台启动 Codex。
- prompt 为空时仍禁止启动，这是当前明确回退后的行为。
- ongoing sessions 列表显示当前 workspace 下由 HaloCreek 创建的 session，标题来自 first prompt summary，支持双击 Bring to Front 和 Exit。

### 3.3 History Sessions

- `CodexSessionHistoryReader` 读取 `$CODEX_HOME/sessions` 或 `~/.codex/sessions` 下的 JSONL 文件。
- 默认只读取最新的 `MaxSessionHistoryFiles=100` 个文件。
- 按 `session_meta.payload.cwd` 与当前 workspace 做严格等价匹配，支持 Windows 路径和 WSL `/mnt/<drive>/...` 路径等价。
- 解析并展示 session id、创建时间、最后更新时间、Initial Prompt、Last Prompt、Last Reply 和 session 文件路径。
- 搜索只过滤已加载 session 的 `InitialPrompt`，不做全文检索。
- 定时刷新已接通，刷新失败时保留旧数据并写日志。
- `Resume` 会在当前 workspace 中执行 `codex resume <session-id>`，并纳入 ongoing 管理。
- `Reedit` 会把 Initial Prompt 填回 Prompt Editor，并切换到 Prompt Editor，不自动 launch。

### 3.4 Ongoing Sessions / tmux / Terminal

- `SessionLifecycleService` 维护 HaloCreek 当前进程创建的 session 表，所有直接状态修改要求在 UI 线程执行。
- `TmuxService` 负责创建 `halocreek-{workspaceHash}-{shortId}` tmux session、kill session、写入 tmux metadata、维护前台 client 和 keeper session。
- `TerminalService` 维护应用实例级 Windows Terminal identity，首次打开固定前台 client，后续复用或拉回。
- Bring to Front 使用固定 WT/tmux client 展示目标 session；原 front session 会切回后台并恢复 watch。
- watch 当前使用 tmux `pipe-pane` heartbeat 文件和 timer 轮询判断 `BackgroundRunning` / `BackgroundIdle` / `Unknown`，不解析 Codex 内部状态。
- 应用退出通过 `AppServices.Dispose()` 释放 `SessionHistoryRefreshService`、`SessionLifecycleService` 和 `TmuxService`，best effort 清理当前进程维护的 tmux sessions 和 keeper session。

### 3.5 Git File Browser

- `GitService.GetChanges(...)` 已接入 `git status --porcelain=v1 -z --untracked-files=all`。
- 支持 modified、added、deleted、renamed、copied、untracked、conflicted 等基础状态映射。
- Git tab 切换或 workspace 切换会刷新；也支持手动 `Refresh`。
- 当前不是 Git repository 时展示明确空态，不伪装为“无变更”。
- 工具栏动作和列表双击动作由配置决定，不再写死五套专用命令。
- 默认动作包括 `OpenDiff`、`Open`、`ExploreTo`、`Edit`、`Commit`、`Show Log`。
- 外部动作只负责启动进程，不等待退出码，不推断 diff、commit 或 open 是否业务成功。

## 4. 架构与边界沉淀

MVP1 已经形成几条后续应继续遵守的边界：

- ViewModel 不直接解析外部格式，不直接调用 `Process.Start`。
- Codex JSONL 格式只由 `CodexSessionHistoryReader` 感知。
- tmux 命令、Terminal 呈现和业务生命周期分属 `TmuxService`、`TerminalService`、`SessionLifecycleService`。
- workspace 与有效配置由 `WorkspaceRuntimeService` 统一分发，不再在各 tab 间传来传去。
- 平台相关路径、WSL、Windows Terminal、弹窗、目录选择收敛在 `PlatformInfrastructure` 和上层 runtime service 中。
- 一过性用户动作失败通过 `TransientEventService` 弹窗；持续性全局状态通过 `ApplicationStatusService` 驱动 footer。

这些边界对 MVP2 很重要，因为审阅能力会同时碰到 Git diff、历史记录、prompt 重新编辑、外部进程和状态反馈。如果边界不继续收敛，MVP2 很容易把“审阅”做成跨 tab 的临时耦合。

## 5. 与最初 MVP1 计划的差异

- Tab 数量从最初 4 个变为 3 个：Ongoing 被合并进 Prompt Editor。
- Git 文件动作从固定 `OpenDiff / Commit / ExploreTo / Open / Edit` 变为配置驱动动作系统。
- Status bar 不再承载普通操作成功、取消、外部动作已启动等一过性消息。
- `SessionLifecycleService.Cleanup()` 没有保留为公开 API，实际清理由 `IDisposable.Dispose()` 承担。
- Ongoing watch 没有继续使用 tmux control mode，也不是当前文档中旧记录的 capture-pane snapshot 模型，而是基于 `pipe-pane` heartbeat。
- Git action 文档曾规划 `{SelectedRelativePath}`、`{SelectedOriginalPath}`、`{SelectedDisplayPath}` 等 token；当前代码实际只替换 `{WorkspaceRoot}` 和 `{SelectedPath}`，按钮可用性也只识别 `{SelectedPath}`。
- 默认 Git 动作比旧方案多了 `Show Log`。

## 6. 已知限制

- 没有自动化测试项目；当前质量主要来自构建验证、人工试用和边界收敛。
- 端到端人工验收记录分散在各阶段文档和 commit 中，缺少一份统一验收清单。
- Codex 历史解析强依赖当前 JSONL 格式；未来 Codex 格式变化时只保证 reader 层可替换，不保证无感兼容。
- History 只做 Initial Prompt 搜索，不做 transcript 全文检索。
- Ongoing 只管理当前 HaloCreek 进程创建的 tmux sessions，不扫描、不恢复、不接管孤儿 session。
- 用户手动 kill、rename、detach tmux，或手动关闭 Windows Terminal 后，ongoing 内部状态不保证实时正确。
- tmux launch task 是异步执行，部分启动失败路径不会在 UI 里形成完整可感知结果。
- Git 外部动作只判断进程是否成功启动，不判断外部工具是否完成真实业务。
- Git action token 能力还小于配置方案文档。
- 配置读取失败当前会 fallback 到默认配置，适合 MVP1 快速使用，但对 MVP2 的审阅工作可能需要更明确地暴露配置损坏。

## 7. MVP2 交接建议

MVP2 主题是“审阅”。建议在进入实现前先把 MVP2 的审阅对象和操作闭环写清楚，不要直接扩散成完整 Git 客户端或任务编排系统。

建议优先复用 MVP1 已有能力：

- 用 Git File Browser 的变更列表作为审阅入口。
- 复用配置驱动外部动作，不急于内置 diff。
- 复用 Prompt Editor 的 reedit/launch 能力来发起“带批注的新一轮修改”。
- 复用 History Sessions 来追踪审阅前后的 session 上下文。
- 复用 `TransientEventService` / `ApplicationStatusService` 的错误分层，不把审阅流程中的每一步都塞进 footer。

## 8. 结论

MVP1 已经验证了 HaloCreek 的核心判断：不做重量级自动编排，也可以通过 workspace、session、history、git 和外部工具的轻量组合，明显降低 Human in the loop 的操作摩擦。

MVP2 应继续保持“一个版本只做一个话题”的约束，把焦点放在 AI 修改的人工审阅闭环上：快速看到变更、快速标记、快速补充批注、快速发起下一轮修改，并允许随时回退到 Prompt Editor 重新约束。
