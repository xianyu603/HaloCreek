# HaloCreek Feature Guide

HaloCreek 是一个面向 Windows + WSL + Codex CLI 的本地桌面工作流壳层，它把 workspace 选择、prompt 启动、session 恢复、Git 修改、Review 状态和日志集中到一个轻量入口里。

启动、依赖检查、workspace 规则和配置说明见 [Startup Guide](StartupGuide.md)。

## 1. Prompt Editor

Prompt Editor 用来编辑 prompt、启动新的 Codex session、向当前前台 session 追加消息，并管理当前 workspace 下由 HaloCreek 启动的 ongoing sessions。

编辑区支持直接输入多行 prompt，也支持把文件或目录拖入编辑区。拖入时会把第一个本地路径追加到 prompt 文本末尾。

可用操作：

- `Launch`：用当前 prompt 启动新的 Codex session。快捷键是 `Ctrl+Enter`。
- `SendToFront`：把当前 prompt 发送到当前前台 session。快捷键是 `Alt+Enter`。
- ongoing session 双击：把该 session 切换到前台 Windows Terminal 窗口。
- ongoing session 的 `Exit`：结束对应 tmux session，并从列表移除。

`Launch` 的执行流程是：在默认 WSL 发行版内创建 tmux session，在 workspace 对应的 WSL 路径下运行 `codex <配置参数> <prompt>`，然后打开或复用 HaloCreek 当前实例的 Windows Terminal 前台窗口。

ongoing session 的标题来自首次 prompt 的前 20 个字符。状态含义：

- `Launching`：tmux session 正在创建，尚不能切到前台。
- `Front`：当前 session 正在前台终端窗口中显示。
- `BackgroundRunning`：后台 session 最近仍有活动信号。
- `BackgroundIdle`：后台 session 最近没有活动信号。
- `Unknown`：状态探测不到或外部 tmux 状态已经变化。

相关配置见 [Startup Guide 的 Codex 启动配置](StartupGuide.md#33-codex-启动配置)。

## 2. Review

Review 用来在 Agent 修改代码后做人工审阅。页面分为三块：左上 Modified Git 列表、左下 Review 状态列表、右侧 prompt 与上下文状态。

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

这些 Git 动作当前使用内置默认配置，依赖 Windows PATH 上的 `TortoiseGitProc.exe`、`explorer.exe`、`notepad.exe`、`git` 和 `cmd.exe`。依赖检查见 [Startup Guide 的依赖检查](StartupGuide.md#11-运行依赖检查)。

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

`Reviewed` 列表表示：文件已经有 Reviewed 快照，并且 Reviewed 快照与 HEAD 不同。它通常用于确认“已审阅但尚未提交”的内容。

常用操作：

- 双击文件：打开 `Reviewed vs HEAD` diff。
- `Diff Against HEAD`：比较 Reviewed 快照与 HEAD。
- `Diff Against Working Tree`：比较 Reviewed 快照与当前工作区。
- `Mark Unreviewed`：移除该文件的 Reviewed 记录，使其重新进入待审阅逻辑。
- `All Unreviewed`：移除所有 Reviewed 记录。
- `Refresh`：刷新列表。

Review 页面会定时刷新，当前间隔为 10 秒；切换到 Review Tab 或切换 workspace 时也会触发刷新。

### 2.3 ClipLocate

右上角的 `Matched` / `Unmatched` 状态来自剪贴板监听。每次剪贴板文本变化时，HaloCreek 会尝试在当前 Git 变更文件中查找该文本。

匹配规则：

- 只搜索 Added、Modified、Renamed、Copied、Untracked 这些变更类型。
- 跳过目录、不存在的文件和超过 2 MB 的文件。
- 会统一换行符，并忽略剪贴板末尾换行。
- 只有当剪贴板文本在所有候选文件中唯一出现一次时，才显示 `Matched`。
- 没有匹配、多处匹配、剪贴板为空或扫描失败时显示 `Unmatched`。

双击 `Matched` / `Unmatched` 状态块，会弹窗显示匹配文件、行列范围或失败原因。

### 2.4 Review Prompt

右侧文本框用于给当前前台 session 发送审阅意见，或基于审阅意见启动新 session。

可用操作：

- `SendToFront`：把 Review prompt 发送到当前前台 session。快捷键是 `Alt+Enter`。
- `Launch New`：用 Review prompt 启动新的 Codex session。快捷键是 `Ctrl+Enter`。
- 双击 `Front Session`：激活当前前台 Windows Terminal 窗口。

`SendToFront` 只有在存在前台 session 且 Review prompt 非空时可用。`Launch New` 只要求 Review prompt 非空。

## 3. History

History 展示 Codex CLI 的历史 session，并且只保留当前 workspace 对应的记录。

HaloCreek 会优先查找以下位置：

- 当前 Windows 环境变量 `CODEX_HOME` 下的 `sessions`。
- 默认 WSL 发行版环境变量 `CODEX_HOME` 下的 `sessions`。
- 当前 Windows `HOME`、默认 WSL `HOME`、Windows 用户目录下的 `.codex\sessions`。

在 Windows 侧读取 WSL 路径时，会尝试使用 `\\wsl.localhost\<DistributionName>\...` 和 `\\wsl$\<DistributionName>\...` 形式访问。

页面字段：

- `Time`：session 创建时间和最后更新时间。
- `Initial Prompt`：首次用户 prompt。
- `Latest Activity`：最近一次用户 prompt 和最近一次 assistant reply。
- 底部只读文本框：选中 session 的完整摘要，包括 Initial Prompt、Last Prompt 和 Last Reply。

可用操作：

- 搜索框：按 Initial Prompt 做大小写不敏感过滤。
- `Resume`：启动 `codex <配置参数> resume <sessionId>`，并把恢复后的进程纳入 ongoing session 管理。
- `Reedit`：把选中 session 的 Initial Prompt 填回 Prompt Editor，并切换到 Prompt Editor Tab。

History 每 10 秒自动刷新一次，最多读取的历史文件数量由 `MaxSessionHistoryFiles` 控制。见 [Startup Guide 的 History 配置](StartupGuide.md#34-history-配置)。

## 4. AppLogs

AppLogs 展示当前应用进程内的日志快照，适合排查依赖、workspace、Git 动作、Review、History 和 session 相关问题。

可用操作：

- `Clear`：清空当前 UI 中展示的日志文本。

日志文本框只读，保留横向和纵向滚动条。长路径、外部命令参数和失败原因通常会在这里出现。

## 5. Workspace Footer

窗口底部 Footer 显示当前 workspace 路径、全局状态文本和 `Select Workspace` 按钮。

`Select Workspace` 的行为与启动时选择 workspace 相同：只接受已存在、可访问的 Git 仓库根目录。切换失败时会弹窗显示原因，并继续使用旧 workspace。

详细 workspace 规则见 [Startup Guide 的启动与 Workspace](StartupGuide.md#2-启动与-workspace)。
