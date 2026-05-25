# MVP1 阶段 6：Git File Browser MVP 实施方案

## 1. 目标

阶段 6 在当前工作区 UI 骨架上接通 Git 页签的最小可用闭环：

- 展示当前 workspace 的 Git 变更文件列表。
- 支持右上角 `Refresh` 手动刷新变更列表。
- 选中一个已跟踪变更文件后，点击左侧工具栏 `OpenDiff` 打开外部 diff 工具。
- 点击左侧工具栏 `Commit` 打开外部 Git commit 窗口。
- `ExploreTo`、`Open`、`Edit` 作为当前 UI 中的文件动作入口，本阶段需要接通业务；具体行为口径在对应任务实施时确认。
- 操作结果同步到 Footer 全局状态。

本阶段对齐 `mvp1.txt` 中“简单 list 展示和接入”的范围，只做查看、基础文件动作、diff 和提交入口，不实现 stash、不实现 add/unstage、不实现内置 diff 预览、不做分支切换、push/pull、冲突解决或完整 Git 客户端能力。

## 2. 当前基础

已有代码边界：

- `MainWindow.axaml`：主窗口为 `TabControl + WorkspaceFooterView`，Git 页签绑定 `GitViewModel`。
- `GitView.axaml`：已有工作区统一布局，外层 `Margin="20"`，顶部工具栏左侧为 `OpenDiff`、`Commit`、`ExploreTo`、`Open`、`Edit`，右侧为 `Refresh`，中间为列表占位区域。
- `GitViewModel`：已有 `WorkspacePath`、`Changes`、`SetWorkspacePath(...)`、`RefreshChanges()` 和 `RefreshCommand`。
- `GitService`：已有 `GetChanges(...)`、`TryOpenDiffTool(...)`、`TryCommit(...)` 占位接口；本阶段允许把占位 bool/list 返回值收敛成显式结果模型。
- `GitChangeInfo`：已有 `RelativePath`、`ChangeType`、`IsStaged`、`OriginalRelativePath` 展示模型。
- `AppConfig` / `ConfigService`：已有 `DiffToolPath` 配置字段，可用于外部 diff 工具路径。
- `MainWindowViewModel`：workspace 切换时已经调用 `Git.SetWorkspacePath(workspacePath)`。
- `WorkspaceFooterViewModel`：已有 workspace 路径展示和全局 `StatusText` 展示，Git 页签状态应通过 dispatcher 写入这里。

阶段 6 优先沿用这些边界。UI 和 ViewModel 不直接解析 Git 命令输出，不直接调用 `Process.Start`。

## 3. 已确认关键决策

- 变更来源先用 `git status --porcelain=v1 -z`，解析成稳定的 `GitChangeInfo` 列表。
- `GitService` 负责 Git 命令执行、输出解析、打开 diff/commit 工具和错误边界；`GitViewModel` 只负责命令、选中项、状态分发。
- workspace 必须是有效 Git work tree；不是 Git 仓库时展示空列表，并在列表空态和 Footer 中给出明确说明。
- 列表同时展示 staged 和 unstaged 变更，用 `IsStaged` 标记；MVP 不提供 stage/unstage 操作。
- 未跟踪文件允许进入列表，但选中时 `OpenDiff` 不可用或执行时返回明确失败状态。
- commit 入口优先尝试 TortoiseGit commit 窗口；不可用时返回失败状态，不静默改用命令行提交。
- diff 工具优先使用 `AppConfig.DiffToolPath`；该字段由全局配置和 workspace `.HaloCreek/config.json` 共同决定。
- 刷新采用手动按钮加 workspace 切换触发，不做定时刷新或文件系统 watch。
- 当前 UI 上的 `ExploreTo`、`Open`、`Edit` 纳入 MVP1-6 任务拆分，作为选中文件的外部文件动作入口；具体调用方式、失败边界和启用规则在对应任务实施时明确。

## 4. 建议实现

### 4.1 接通 GitService

`GitService.GetChanges(workspacePath)`：

- 校验 workspace path。
- 执行 `git -C <workspace> status --porcelain=v1 -z`。
- 解析 staged / unstaged 两列状态、rename/copy 原路径和目标路径。
- 输出按路径排序的 `GitChangeInfo`。
- 返回显式查询结果，例如 `GitChangesResult(Changes, Status, Message)`，用于区分 loaded / no workspace / not git repository / command failed。
- 命令失败时允许 `Changes` 为空，但 `Status` 和 `Message` 必须说明失败原因；不要把失败静默伪装成“无变更”。

`GitService.TryOpenDiffTool(...)`：

- 建议签名调整为接收 diff tool 配置，例如 `TryOpenDiffTool(workspacePath, change, diffToolPath)`。
- 要求 workspace、变更项和 diff tool 配置有效。
- 拒绝 untracked 文件，并返回可展示的失败原因。
- 对 modified / deleted / renamed / copied 等已跟踪变更调用外部 diff 工具。
- 外部工具不可用、路径不存在或进程启动失败时显式返回失败状态。

`GitService.TryCommit(...)`：

- 要求 workspace 有效。
- 优先打开 TortoiseGit commit 窗口。
- 窗口成功打开只表示提交入口已启动，不假设提交已经完成。
- 返回显式操作结果，例如 `GitOperationResult(Succeeded, Message)`，供 Footer 展示。

### 4.2 接通 GitViewModel

`GitViewModel` 新增或调整：

- `SelectedChange`
- `HasChanges`
- `IsEmptyStateVisible`
- `RefreshCommand`
- `OpenDiffCommand`
- `CommitCommand`
- `ExploreToCommand`
- `OpenCommand`
- `EditCommand`
- `SetStatusDispatcher(...)`
- `ConfigService` 依赖，用于读取当前 workspace 的有效配置。

命令行为：

- `SetWorkspacePath(...)` 保存 workspace 后触发一次刷新，保持当前工作区切换即看到 Git 状态的体验。
- `RefreshCommand` 调用 `RefreshChanges()`，根据 `GitChangesResult` 更新 `Changes`、空态文本和 Footer；非 Git workspace 或命令失败时给出明确状态。
- `OpenDiffCommand` 要求已有 workspace、已选中变更且变更可 diff，读取配置后调用 `GitService.TryOpenDiffTool(...)`，并把操作结果写入 Footer。
- `CommitCommand` 要求已有 workspace 且存在变更，调用 `GitService.TryCommit(...)`，并把操作结果写入 Footer。
- `ExploreToCommand` / `OpenCommand` / `EditCommand` 要求已有 workspace 且已选中变更，具体业务动作、外部程序选择和失败提示在对应任务中确定，并把操作结果写入 Footer。
- `OpenDiff` / `Commit` / `ExploreTo` / `Open` / `Edit` 执行后不自动刷新，除非后续人工验证认为体验明显需要。

### 4.3 完成 GitView

UI 按当前工作区骨架继续推进，不重新设计为另一套布局：

- 外层继续使用 `Margin="20"` 和顶部 `Grid RowDefinitions="Auto,*"`。
- 顶部工具栏继续保持左侧动作组、右侧 `Refresh` 的结构。
- 左侧动作组保留现有顺序：`OpenDiff`、`Commit`、`ExploreTo`、`Open`、`Edit`。
- 本阶段接通 `OpenDiff`、`Commit`、`ExploreTo`、`Open`、`Edit`、`Refresh`；`ExploreTo`、`Open`、`Edit` 的具体业务口径在对应任务中补齐。
- 列表区域替换当前 placeholder，沿用相邻页面的浅色边框样式：`BorderBrush="#CBD5E1"`、`CornerRadius="6"`、白底、`ClipToBounds="True"`。
- 列表顶部增加紧凑表头，建议列为 `Status`、`Stage`、`Path`。
- 每行展示状态、staged 标记、相对路径；rename/copy 时展示 `old -> new`。
- 空列表时显示空状态文本，例如 `No Git changes for current workspace`；非 Git workspace 时显示 `Current workspace is not a Git repository`。

按钮启用规则：

- `Refresh`：有 workspace 时可用。
- `OpenDiff`：有 workspace、已选中变更且不是 untracked 时可用。
- `Commit`：有 workspace 且存在变更时可用。
- `ExploreTo` / `Open` / `Edit`：有 workspace 且已选中变更时按对应任务口径启用；无 workspace 或无选中项时不可用。

## 5. 任务拆分

- [ ] **6-T01 接通 Git 状态读取**：实现 `GitService.GetChanges(...)` 的真实 Git 命令执行和 porcelain 输出解析；验收点是 modified / added / deleted / renamed / copied / untracked 能进入列表，非 Git workspace 不崩溃且不被误报为“无变更”。
- [ ] **6-T02 接通刷新与列表绑定**：完善 `GitViewModel` 的 `RefreshCommand`、`SelectedChange`、`HasChanges`、空态状态和 Footer 状态分发，`GitView` 改为真实列表展示；验收点是切换 workspace 和点击 `Refresh` 都能看到当前变更。
- [ ] **6-T03 按工作区 UI 接通工具栏状态**：保持当前 `OpenDiff / Commit / ExploreTo / Open / Edit / Refresh` 布局，让各按钮按 MVP 规则和对应业务任务启用；验收点是视觉结构不偏离当前工作区 UI，且无 workspace 或无选中项时文件动作不会误启用。
- [ ] **6-T04 接通 OpenDiff**：用配置 diff tool 或 TortoiseGit 打开选中文件 diff；验收点是 modified/deleted/renamed/copied 文件可打开，untracked 文件给出明确失败状态。
- [ ] **6-T05 接通 Commit**：打开 TortoiseGit commit 窗口；验收点是点击 `Commit` 能在当前 workspace 调起提交 UI，窗口打开后不假设提交完成。
- [ ] **6-T06 接通 ExploreTo**：为选中的 Git 变更文件接通 `ExploreTo` 业务入口；具体是定位到文件、定位到目录还是其他平台相关行为在实施时确定，验收点随最终口径补齐。
- [ ] **6-T07 接通 Open**：为选中的 Git 变更文件接通 `Open` 业务入口；具体打开方式、默认程序选择和不存在文件的处理在实施时确定，验收点随最终口径补齐。
- [ ] **6-T08 接通 Edit**：为选中的 Git 变更文件接通 `Edit` 业务入口；具体编辑器来源、配置项复用和不可编辑文件的处理在实施时确定，验收点随最终口径补齐。
- [ ] **6-T09 错误状态与人工验收**：覆盖非 Git workspace、无变更、工具缺失、Git 命令失败、文件动作失败等状态；验收点是 UI 不崩溃，Footer 有可理解状态。

## 6. 验收标准

- 当前 workspace 是 Git 仓库时，Git 页签能展示真实变更列表。
- 当前 workspace 不是 Git 仓库时，不崩溃，列表和 Footer 都有明确说明。
- `Refresh` 位于顶部工具栏右侧，并能手动刷新列表。
- `OpenDiff`、`Commit`、`ExploreTo`、`Open`、`Edit` 的位置和顺序与当前 `GitView.axaml` 一致。
- `ExploreTo`、`Open`、`Edit` 在本阶段接通业务入口；具体行为按对应任务最终口径验收，不出现半可用入口。
- 选中已跟踪变更后，`OpenDiff` 能打开外部 diff 工具或明确报告工具不可用。
- 选中 untracked 变更时，`OpenDiff` 不可用或执行时明确报告不可 diff。
- 点击 `Commit` 能打开外部 commit 窗口或明确报告工具不可用。
- `GitViewModel` 不直接调用 `Process.Start`，不直接解析 Git 输出。
- `GitService` 不依赖 Avalonia 控件、ViewModel 或 tab 状态。
