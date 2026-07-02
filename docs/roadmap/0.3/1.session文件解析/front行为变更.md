# Front 行为变更

## 背景

本版本已经接入前台 session 信息展示，用户在大多数场景下可以通过应用内 session 状态判断 Codex 是否仍在工作，不需要频繁查看 Windows Terminal。

因此，`front` 的产品语义调整为“应用内前台 session”，不再等同于 tmux 前台 client，也不再默认拉起 Windows Terminal。

## 产品结论

### 1. Front 只表示应用内前台

用户双击 Ongoing Sessions 中的 session 时，只切换应用内前台 session。

切换后：

- 右侧 session 信息展示同步切到该 session。
- Prompt 输入区的 `Send to front` 目标同步切到该 session。
- Ongoing Sessions 中该 session 的状态展示为 `Front`。
- 不切换 tmux 前台 client。
- 不拉起 Windows Terminal。
- 不激活已有 Windows Terminal。

`Front` 状态代表应用当前关注和发送消息的 session，不代表终端窗口当前 attach 的 session。

### 2. Launch 后不再自动拉起终端

用户发起 Launch 或 Resume 后，应用只创建/恢复后台 session，并在应用内展示其运行状态。

Launch 或 Resume 完成后：

- 不自动拉起 Windows Terminal。
- 不自动切换 tmux 前台 client。
- session 默认留在后台运行状态，除非用户随后显式将其切为应用内 front。

这样可以让新 session 默认在后台工作，减少终端窗口打断。

### 3. 显式入口拉起 Session CLI

Ongoing Sessions 中增加一个和 `Exit` 并列的操作入口，用于拉起该 session 的 CLI。

用户点击该入口时：

- 如果 HaloCreek 管理的 Windows Terminal 前台 client 不存在，则拉起 Windows Terminal 并 attach 到该 session。
- 如果该 Windows Terminal 前台 client 已存在，则激活该窗口，并一次性切换 tmux 前台 client 到该 session。
- 这次 tmux 切换只服务于本次打开 CLI 的用户动作，不建立应用 front 与 tmux front 的持续同步关系。

该入口是进入终端交互的唯一常规入口。

### 4. 移除 Ctrl+Alt+T 快捷键

本版本目标是大多数时候不需要 Windows Terminal 窗口，因此移除 `Ctrl+Alt+T` 前台终端快捷键。

用户需要查看或操作 CLI 时，应通过 Ongoing Sessions 中的显式 CLI 入口完成。

### 5. Exit 不再为了保留终端而切到 keeper session

旧行为中，退出当前 tmux 前台 session 前会先切换到 keeper session，以避免 Windows Terminal 因 attach 目标结束而关闭。

新行为反向调整：退出 session 后允许 Windows Terminal 自然关闭。下次用户确实需要 CLI 时，再通过显式入口重新拉起。

## Ongoing Sessions 交互调整

Ongoing Sessions 需要更明确地区分“选择应用 front”和“打开 CLI”：

- 双击 session 行：切换应用内 front。
- `CLI` 按钮：拉起或切换到该 session 的终端 CLI。
- `Exit` 按钮：退出该 session。

列表样式需要容纳至少两个并列操作按钮，避免用户把双击行为理解为打开终端。

## 用户可感知变化

- Launch/Resume 后不会再弹出或激活 Windows Terminal。
- 双击 session 只会切换应用内信息展示和发送目标。
- 用户只有点击 session 的 CLI 操作时才会看到 Windows Terminal。
- 退出 session 时，如果该 session 正被 Windows Terminal attach，终端窗口可以随 tmux attach 结束而关闭。
- `Ctrl+Alt+T` 不再可用。

## 非目标

- 不做应用 front 与 tmux front 的持续同步。
- 不保证用户手动在 tmux 内切换 session 后，应用内 front 随之变化。
- 不把 Windows Terminal 作为 session 状态判断的主要入口。
