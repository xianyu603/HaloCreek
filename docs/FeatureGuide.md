# HaloCreek Feature Guide

HaloCreek 是一个面向 Windows + Codex CLI 的本地桌面工作流壳层，它把 workspace 选择、prompt 启动、session 恢复、应用内 session 状态、Git 修改、Review 状态和日志集中到一个轻量入口里。0.3 版本后，Codex session 默认在后台 psmux session 中运行；只有用户显式点击 `CLI` 时才打开前台命令窗口。

启动、依赖安装、workspace 规则和配置说明见 [Startup Guide](StartupGuide.md)。

## 1. Prompt Editor

Prompt Editor 用来编辑 prompt、启动新的 Codex session、向当前应用内前台 session 追加消息，并管理当前 workspace 下由 HaloCreek 启动的 ongoing sessions。

页面左侧是 prompt 输入区，右侧是当前应用内前台 session 的状态视图。没有前台 session 时，右侧显示 `No front session`。

编辑区支持直接输入多行 prompt，也支持把文件或目录拖入编辑区。拖入时会把第一个本地路径追加到 prompt 文本末尾。

粘贴行为：

- 剪贴板包含图片时，会先把图片保存为系统临时目录下的 `HaloCreek\prompt-paste-image-<随机后缀>.png`，再把该图片文件路径插入 prompt。
- 剪贴板没有图片但包含文本时，会把文本插入当前光标位置或替换当前选区。

补全入口：

- 输入 `#` 触发快捷语补全。空 query 时先展示类别；输入类别名或别名可直接展开该类别；输入其他文本时按标题、说明、别名和正文做模糊匹配。快捷语配置见 [Startup Guide 的快捷语配置](StartupGuide.md#36-快捷语配置)。
- 输入 `$` 触发 skill 补全。空 query 时按来源和分类展示；输入来源、分类或 skill 名称可过滤候选；接受候选后写回 `$skill-name`。
- 输入 `@` 触发文件补全。空 query 时优先展示未提交文件和最近提交文件；输入 query 后会在当前 workspace 的文件和目录索引中做模糊匹配，接受候选后写回相对路径。

补全菜单支持键盘操作：`Down` / `Up` 移动候选，`Right` 进入子菜单，`Left` 返回上一级，`Enter` 接受当前候选，`Escape` 回到当前 query 的一级候选。

右键 prompt 输入框可以打开模板菜单：

- `Templates`：插入内置结构化 prompt 模板。
- `Recent Initial Prompts`：插入当前 workspace 最近 Codex history 中的初始 prompt。

菜单选中模板或历史 prompt 时会显示预览；点击候选后把正文插入当前光标位置。

可用操作：

- `Launch`：用当前 prompt 启动新的 Codex session。快捷键是 `Ctrl+Enter`。
- `SendToFront`：把当前 prompt 发送到当前应用内前台 session。快捷键是 `Alt+Enter`。
- ongoing session 双击：把该 session 切为应用内前台 session。
- ongoing session 的 `CLI`：显式打开前台命令窗口，并 attach 到该 session 的 psmux CLI。
- ongoing session 的 `Restart`：仅在 session 被判定为 `Dead` 时显示，用原始 launch prompt 或 Codex session id 重新启动 session。
- ongoing session 的 `Exit`：结束对应 psmux session，并从列表移除。

`Launch` 的执行流程是：在当前 workspace 下创建后台 psmux session，运行 `codex <配置参数> <prompt>`，然后把新 session 设为应用内前台 session。0.3 起不会自动打开前台命令窗口。

ongoing session 的标题来自首次 prompt 的前 20 个字符。应用内 `Front` 只表示 HaloCreek 当前关注和发送消息的 session，不代表当前打开的命令窗口 attach 的 session。用户手动在 psmux/终端里切换 session 时，应用内 front 不会随之同步。

状态含义：

- `Requested`：session 启动请求已提交。
- `Launched`：psmux session 已创建，但尚未匹配到 Codex session 文件。
- `Active`：Codex session 最近仍有写入或活动信号。
- `Idle`：Codex session 最近没有活动信号。
- `Started` / `Completed` / `Aborted`：来自 Codex session JSONL 的任务状态。
- `CLI attention`：进程仍存在，但没有探测到 Codex 输入面板；通常需要点击 `CLI` 检查。
- `Dead`：psmux session 已退出，可选择 `Restart` 或 `Exit`。
- `Unknown`：还没有足够状态信息。

相关配置见 [Startup Guide 的 Codex 启动配置](StartupGuide.md#33-codex-启动配置)。

右侧 session 状态视图读取 Codex session JSONL 文件，展示用户消息、assistant 消息、任务状态、更新时间和 token 摘要。assistant 消息使用 Markdown 渲染；本地文件链接会优先按文件路径打开，带行号的链接会尝试根据 `MarkdownLineJumpCommands` 配置打开编辑器。

### 1.1 全局快捷键与悬浮输入框

HaloCreek 启动后会在 Windows 桌面环境注册以下全局快捷键：

- `Ctrl+Alt+H`：打开或激活 HaloCreek 悬浮 prompt 输入框，并把焦点放到输入框中。

悬浮 prompt 输入框复用 Prompt Editor 的输入能力，包括 `Ctrl+Enter` 启动新 session、`Alt+Enter` 发送到当前前台 session、`#` / `$` / `@` 补全、右键模板和粘贴图片。通过悬浮框提交 prompt 后，悬浮框会自动隐藏。

0.3 起不再提供 `Ctrl+Alt+T` 打开前台终端的全局快捷键；需要 CLI 时从 Ongoing Sessions 的 `CLI` 按钮进入。

## 2. Review

Review 用来在 Agent 修改代码后做人工审阅。页面分为上方 Modified Git 列表和下方 Review 状态列表。

### 2.1 Modified

Modified 展示当前 workspace 的 Git 修改，来源等价于：

```bash
git status --porcelain=v1 -z --untracked-files=all
```

支持的状态标记包括：

- `A`：新增。
- `M`：修改。
- `D`：删除。
- `R`：重命名。
- `C`：复制。
- `?`：未跟踪。
- `U`：冲突。

在文件行上双击时，默认执行 `Open` 动作，即用 `explorer.exe` 打开选中文件。右键文件可以执行以下默认动作：

- `OpenDiff`：用 TortoiseGit 打开该文件 diff。
- `Open`：用 Explorer 打开该文件。
- `ExploreTo`：在 Explorer 中定位该文件。
- `Edit`：用 Notepad 打开该文件。
- `Revert`：执行 `git restore --source=HEAD --staged --worktree -- <SelectedPath>`。
- `Delete`：执行 `cmd.exe /c del /f /q <SelectedPath>`。

在 Modified 空白区域右键，可以执行 workspace 级动作：

- `Refresh`：刷新 Review 和 Modified 状态。
- `Commit`：用 TortoiseGit 打开当前 workspace 的 commit 窗口。
- `Show Log`：用 TortoiseGit 打开当前 workspace 的 log 窗口。

这些 Git 动作当前使用内置默认配置，依赖 Windows PATH 上的 `TortoiseGitProc.exe`、`explorer.exe`、`notepad.exe`、`git` 和 `cmd.exe`。依赖安装与检查见 [Startup Guide 的依赖安装与检查](StartupGuide.md#1-依赖安装与检查)。

### 2.2 Unreviewed 与 Reviewed

Review 状态使用 workspace 根目录下的独立 Git index 文件保存：

```text
.HaloCreek\HaloCreekIndex
```

这个文件不是普通 Git index，不会直接改变你的主 index。它记录“某个文件在人工审阅通过时的 blob”。HaloCreek 会用它比较工作区、Reviewed 快照和 HEAD。

`Unreviewed` 列表表示：工作区文件与 Reviewed 快照不同，仍需要人工审阅。对于还没有 Reviewed 快照的文件，会与 HEAD 比较。

常用操作：

- 双击文件：打开 `Working Tree vs Reviewed` diff。
- `Diff Against Reviewed`：同双击。
- `Mark Reviewed`：把当前工作区文件内容记录为已审阅版本。
- `Revert to Reviewed`：把工作区文件恢复到已审阅版本；如果没有已审阅版本，则尝试恢复到 HEAD；如果 HEAD 中也不存在，则删除工作区文件。
- `All Reviewed`：把所有 Unreviewed 文件标记为 Reviewed。
- `Revert All to Reviewed`：批量恢复所有 Unreviewed 文件。
- `Refresh`：刷新列表。

`Reviewed` 列表表示：文件已经有 Reviewed 快照，并且该 Reviewed 快照仍是当前有效审阅结果。它通常用于确认“已审阅但尚未提交”的内容。

常用操作：

- 双击文件：打开 `Reviewed vs HEAD` diff。
- `Diff Against HEAD`：比较 Reviewed 快照与 HEAD。
- `Diff Against Working Tree`：比较 Reviewed 快照与当前工作区。
- `Mark Unreviewed`：移除该文件的 Reviewed 记录，使其重新进入待审阅逻辑。
- `All Unreviewed`：移除所有 Reviewed 记录。
- `Refresh`：刷新列表。

Review 页面会通过 workspace snapshot 自动刷新；切换到 Review Tab 或切换 workspace 时也会触发刷新。

0.3 当前 Review 页没有独立的右侧 prompt 面板。审阅后需要追问或要求 Agent 修改时，在 Prompt Editor 或悬浮输入框中用 `Alt+Enter` 发送到当前应用内前台 session。

## 3. History

History 展示 Codex CLI 的历史 session，并且只保留当前 workspace 对应的记录。

HaloCreek 会优先查找以下位置：

- 当前 Windows 环境变量 `CODEX_HOME` 下的 `sessions`。
- 当前 Windows 用户目录下的 `.codex\sessions`。
- 默认 WSL 发行版环境变量 `CODEX_HOME` 下的 `sessions`。
- 默认 WSL `HOME` 下的 `.codex\sessions`。

在 Windows 侧读取 WSL 路径时，会尝试使用 `\\wsl.localhost\<DistributionName>\...` 形式访问。

页面字段：

- `Time`：session 创建时间和最后更新时间。
- `Initial Prompt`：首次用户 prompt。
- `Latest Activity`：最近一次用户 prompt 和最近一次 assistant reply。
- 底部只读文本框：选中 session 的完整摘要，包括 Initial Prompt、Last Prompt 和 Last Reply。

可用操作：

- 搜索框：按 Initial Prompt 做大小写不敏感过滤。
- `Resume`：启动 `codex <配置参数> resume <sessionId>`，并把恢复后的进程纳入 ongoing session 管理。

History 通过 workspace snapshot 自动刷新，最多读取的历史文件数量由 `MaxSessionHistoryFiles` 控制。见 [Startup Guide 的 History 配置](StartupGuide.md#34-history-配置)。

## 4. AppLogs

AppLogs 展示当前应用进程内的日志快照，适合排查依赖、workspace、Git 动作、Review、History 和 session 相关问题。

可用操作：

- `Clear`：清空当前 UI 中展示的日志文本。

日志文本框只读，保留横向和纵向滚动条。长路径、外部命令参数和失败原因通常会在这里出现。

## 5. Workspace Footer

窗口底部 Footer 显示当前 workspace 路径、全局状态文本和 `Select Workspace` 按钮。

`Select Workspace` 的行为与启动时选择 workspace 相同：只接受已存在、可访问的 Git 仓库根目录。切换失败时会弹窗显示原因，并继续使用旧 workspace。

详细 workspace 规则见 [Startup Guide 的启动与 Workspace](StartupGuide.md#2-启动与-workspace)。
