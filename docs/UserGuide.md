# HaloCreek 使用手册

HaloCreek 是一个面向 Windows + WSL + Codex CLI 的本地桌面工作流壳层，它把 workspace 选择、prompt 启动、session 恢复、Git 修改、Review 状态和日志集中到一个轻量入口里。

当前版本的运行前提：应用启动后必须有一个有效的 Git 仓库根目录作为 workspace；Codex session 在默认 WSL 发行版内运行；前台交互窗口由 Windows Terminal 打开；Diff 和部分 Git 操作依赖 TortoiseGit。

## 1. 依赖检查与安装

### 1.1 运行依赖检查

在 Windows 侧打开 PowerShell 或命令提示符，进入仓库目录后运行：

```bat
scripts\check_deps.bat
```

检查全部通过时，脚本会输出多行 `[PASS] ... - OK` 并以退出码 `0` 结束。任意检查失败时，会输出 `[FAIL]`，并在同一行显示脚本期望的能力、实际退出码和命令输出；脚本最终以退出码 `1` 结束。

### 1.2 检查项说明

#### 1.2.1 WSL default distribution

通过时输出：

```text
[PASS] WSL default distribution - OK
```

这一项执行 `wsl.exe --exec bash -ic "printf halocreek-wsl-ok"`，用于确认 Windows 可以启动默认 WSL 发行版，并且发行版内有可用的 `bash`。HaloCreek 后续启动 Codex、tmux、读取 WSL 环境变量和转换路径时都依赖默认 WSL 发行版。

失败时，通常需要先安装 WSL 和 Linux 发行版，并确认 `wsl.exe` 可用：

```powershell
wsl --install
wsl --status
wsl --list --verbose
```

如果机器上有多个发行版，建议把日常使用的发行版设为默认发行版：

```powershell
wsl --set-default <DistributionName>
```

#### 1.2.2 WSL tmux

通过时输出：

```text
[PASS] WSL tmux - OK
```

这一项在默认 WSL 发行版内执行 `tmux -V`，用于确认 `tmux` 已安装。HaloCreek 的 `Launch` 和 `Resume` 会先在 WSL 内创建后台 tmux session，再把其中一个 session 切到前台 Windows Terminal 窗口。

失败时，在默认 WSL 发行版中安装 tmux。例如 Ubuntu/Debian：

```bash
sudo apt update
sudo apt install tmux
tmux -V
```

#### 1.2.3 WSL Codex CLI

通过时输出：

```text
[PASS] WSL Codex CLI - OK
```

这一项在默认 WSL 发行版内执行 `codex --version`，并要求输出形如 `codex-cli x.y...`。HaloCreek 不内置 Codex 执行能力，实际任务由 WSL 内的 Codex CLI 完成。

失败时，需要在默认 WSL 发行版中安装并登录 Codex CLI。安装完成后确认：

```bash
codex --version
```

如果你的 Codex 可执行文件不叫 `codex`，可以通过配置项 `CodexExecutableName` 修改。见 [4. 配置说明](#4-配置说明)。

#### 1.2.4 Windows Git on PATH

通过时输出：

```text
[PASS] Windows Git on PATH - OK
```

这一项在 Windows 侧执行 `git.exe --version`。HaloCreek 在 Windows 进程里读取当前 workspace 的 Git 状态、HEAD 内容、工作区文件 hash，并执行部分 Git 文件操作，所以 Windows PATH 上必须能找到 `git.exe`。

失败时，安装 [Git for Windows](https://git-scm.com/install/windows)，并确认新开一个 PowerShell 后可执行：

```powershell
git.exe --version
```

#### 1.2.5 TortoiseGitProc on PATH

通过时输出：

```text
[PASS] TortoiseGitProc on PATH - OK
```

这一项在 Windows 侧执行 `where.exe TortoiseGitProc.exe`。HaloCreek 当前使用 TortoiseGitProc 打开 diff、commit 和 log 等外部 Git 窗口。

失败时，安装 [TortoiseGit](https://tortoisegit.org/download/)，并把安装目录加入 Windows PATH。常见安装目录类似：

```text
C:\Program Files\TortoiseGit\bin
```

确认方式：

```powershell
where.exe TortoiseGitProc.exe
```

## 2. Startup

### 2.1 首次启动必须选择 workspace

HaloCreek 启动时会先初始化平台能力、剪贴板监听、配置服务和 workspace runtime，然后进入 workspace 选择流程。业务页面在有效 workspace 建立之后才会创建。

首次启动时没有缓存的 workspace，应用会弹出目录选择器。必须选择一个已经存在、可访问的 Git 仓库根目录。取消选择不会进入主界面，应用会提示必须选择 Git 仓库根目录才能继续。

### 2.2 workspace 当前限制

当前 workspace 只支持完整 Git 仓库根目录，不支持普通目录，也不支持仓库内的子目录。

选择目录后，HaloCreek 会在该目录运行：

```bash
git rev-parse --show-toplevel
```

如果解析出的 Git root 与用户选择目录不同，说明用户选择的是仓库子目录，应用会拒绝切换，并提示应该选择 Git 仓库根目录。

这个限制影响所有功能：

- Prompt Editor 的 `Launch` 会在当前 workspace 下启动 Codex。
- Review 和 Modified 列表只读取当前 workspace 的 Git 状态。
- History 只展示与当前 workspace 路径匹配的 Codex 历史 session。
- workspace 级配置只从当前 workspace 根目录下的 `.HaloCreek\config.json` 读取。

### 2.3 上次 workspace 缓存

每次成功选择 workspace 后，路径会写入用户 AppData 下的缓存文件：

```text
%APPDATA%\HaloCreek\workspace-cache.json
```

下次启动时，HaloCreek 会优先尝试使用上次 workspace。如果该路径已经不存在、不可访问、不是 Git 仓库根目录，或者 Git root 校验失败，应用会提示缓存 workspace 不再可用，并要求重新选择。

### 2.4 运行中切换 workspace

主窗口底部显示当前 workspace 路径，并提供 `Select Workspace` 按钮。切换规则与首次启动相同：只能选择 Git 仓库根目录。

切换成功后：

- 底部 workspace 路径更新。
- History 会按新 workspace 重新读取历史记录。
- Review 会按新 workspace 刷新 Git 修改和 Review 状态。
- 不属于新 workspace 的 ongoing session 会被退出并从列表移除。

切换失败时，旧 workspace 保持不变。

## 3. 功能介绍

### 3.1 Prompt Editor

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

相关配置见 [4.3 Codex 启动配置](#43-codex-启动配置)。

### 3.2 Review

Review 用来在 Agent 修改代码后做人工审阅。页面分为三块：左上 Modified Git 列表、左下 Review 状态列表、右侧 prompt 与上下文状态。

#### 3.2.1 Modified

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

这些 Git 动作当前使用内置默认配置，依赖 Windows PATH 上的 `TortoiseGitProc.exe`、`explorer.exe`、`notepad.exe`、`git` 和 `cmd.exe`。依赖检查见 [1. 依赖检查与安装](#1-依赖检查与安装)。

#### 3.2.2 Unreviewed 与 Reviewed

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

#### 3.2.3 ClipLocate

右上角的 `Matched` / `Unmatched` 状态来自剪贴板监听。每次剪贴板文本变化时，HaloCreek 会尝试在当前 Git 变更文件中查找该文本。

匹配规则：

- 只搜索 Added、Modified、Renamed、Copied、Untracked 这些变更类型。
- 跳过目录、不存在的文件和超过 2 MB 的文件。
- 会统一换行符，并忽略剪贴板末尾换行。
- 只有当剪贴板文本在所有候选文件中唯一出现一次时，才显示 `Matched`。
- 没有匹配、多处匹配、剪贴板为空或扫描失败时显示 `Unmatched`。

双击 `Matched` / `Unmatched` 状态块，会弹窗显示匹配文件、行列范围或失败原因。

#### 3.2.4 Review Prompt

右侧文本框用于给当前前台 session 发送审阅意见，或基于审阅意见启动新 session。

可用操作：

- `SendToFront`：把 Review prompt 发送到当前前台 session。快捷键是 `Alt+Enter`。
- `Launch New`：用 Review prompt 启动新的 Codex session。快捷键是 `Ctrl+Enter`。
- 双击 `Front Session`：激活当前前台 Windows Terminal 窗口。

`SendToFront` 只有在存在前台 session 且 Review prompt 非空时可用。`Launch New` 只要求 Review prompt 非空。

### 3.3 History

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

History 每 10 秒自动刷新一次，最多读取的历史文件数量由 `MaxSessionHistoryFiles` 控制。见 [4.4 History 配置](#44-history-配置)。

### 3.4 AppLogs

AppLogs 展示当前应用进程内的日志快照，适合排查依赖、workspace、Git 动作、Review、History 和 session 相关问题。

可用操作：

- `Clear`：清空当前 UI 中展示的日志文本。

日志文本框只读，保留横向和纵向滚动条。长路径、外部命令参数和失败原因通常会在这里出现。

### 3.5 Workspace Footer

窗口底部 Footer 显示当前 workspace 路径、全局状态文本和 `Select Workspace` 按钮。

`Select Workspace` 的行为与启动时选择 workspace 相同：只接受已存在、可访问的 Git 仓库根目录。切换失败时会弹窗显示原因，并继续使用旧 workspace。

## 4. 配置说明

### 4.1 配置文件位置与优先级

HaloCreek 当前支持两级配置：

全局配置：

```text
%APPDATA%\HaloCreek\config.json
```

workspace 配置：

```text
<WorkspaceRoot>\.HaloCreek\config.json
```

加载顺序是：先使用内置默认值，再合并全局配置，最后合并 workspace 配置。workspace 配置优先级最高。

配置文件是 JSON，支持注释和尾随逗号，属性名大小写不敏感。

示例：

```json
{
  "CodexExecutableName": "codex",
  "CodexLaunchArguments": [],
  "MaxSessionHistoryFiles": 100
}
```

当前版本不会在运行中热重载配置。修改配置后，需要重新启动应用，或至少重新选择 workspace 以重新加载 effective config。

### 4.2 默认配置

内置默认值为：

```json
{
  "CodexExecutableName": "codex",
  "CodexLaunchArguments": [],
  "MaxSessionHistoryFiles": 100
}
```

缺失字段会继承低优先级配置或默认值。`CodexExecutableName` 为空白时会被忽略；`MaxSessionHistoryFiles` 必须大于 0，否则会继承低优先级配置或默认值。

### 4.3 Codex 启动配置

`CodexExecutableName` 控制 HaloCreek 在 WSL tmux session 内启动的 Codex 可执行文件名。默认值是 `codex`。

关联功能：

- Prompt Editor 的 `Launch`。
- Review 的 `Launch New`。
- History 的 `Resume`。

`CodexLaunchArguments` 会追加到 Codex 可执行文件之后、具体 prompt 或 `resume <sessionId>` 之前。

例如：

```json
{
  "CodexExecutableName": "codex",
  "CodexLaunchArguments": ["--model", "gpt-5-codex"]
}
```

Prompt Editor 的 `Launch` 最终类似：

```bash
codex --model gpt-5-codex "<prompt>"
```

History 的 `Resume` 最终类似：

```bash
codex --model gpt-5-codex resume <sessionId>
```

这些命令在默认 WSL 发行版内执行，所以可执行文件和相关认证状态也必须存在于默认 WSL 环境中。依赖检查见 [1.2 检查项说明](#12-检查项说明)。

### 4.4 History 配置

`MaxSessionHistoryFiles` 控制 History 每次从 Codex sessions 目录中最多读取多少个最新的 `*.jsonl` 文件。默认值是 `100`。

关联功能：

- History 列表展示。
- History 搜索。
- History `Resume` 和 `Reedit` 的可见候选范围。

如果 Codex 历史很多，可以调小这个值以减少读取时间；如果需要回看更早的 session，可以调大这个值。

示例：

```json
{
  "MaxSessionHistoryFiles": 300
}
```

### 4.5 当前不可配置项

以下行为当前是内置实现，不通过 `config.json` 配置：

- Modified 列表的右键动作集合和双击默认动作。
- Review 自动刷新间隔，当前为 10 秒。
- History 自动刷新间隔，当前为 10 秒。
- ClipLocate 的候选文件大小上限，当前为 2 MB。
- 前台终端程序，当前固定调用 `wt.exe`。
- 外部 diff 程序，当前固定调用 `TortoiseGitProc.exe`。

这些项如果后续需要调整，应先在实现中增加明确的配置字段，再更新本手册。
