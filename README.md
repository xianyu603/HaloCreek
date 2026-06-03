# HaloCreek

HaloCreek 是一个面向个人 Agent 编程工作流的 Codex shell wrapper。它不尝试做完整任务编排，而是把 workspace、prompt、Codex session、历史记录、Git 变更和人工审阅入口放到一个轻量桌面应用里，降低 Human in the loop 场景里的重复操作成本。

当前实现是 Avalonia/.NET 桌面应用，主要面向 Windows + WSL 环境：Codex session 通过 WSL 内的 `tmux` 后台运行，前台交互通过 Windows Terminal 拉起或复用同一个窗口。

## 当前能力

- Workspace：选择当前工作目录，并缓存上次使用的 workspace。
- Prompt Editor：编辑多行 prompt，支持拖入文件/目录追加路径，一键启动 Codex session。
- Ongoing Sessions：在 Prompt Editor 中展示当前 HaloCreek 进程创建的 session，支持拉到前台和退出。
- Review：监听剪贴板上下文，定位被复制内容对应的文件位置，并向前台 session 发送补充 prompt。
- Git：读取当前 workspace 的 Git 变更，支持配置驱动的外部动作，例如 diff、打开文件、定位文件、commit、show log。
- History：读取本机 Codex JSONL session 历史，按当前 workspace 过滤，支持搜索、resume 和重新编辑初始 prompt。
- AppLogs：查看应用内日志。

## 环境要求

- .NET 10 SDK
- Windows Terminal：默认通过 `wt.exe` 打开前台 session。
- WSL：用于执行 shell、`tmux` 和 Codex。
- tmux：HaloCreek 使用 tmux session 管理后台 Codex。
- Codex CLI：默认可执行文件名为 `codex`。
- git：Git 页签通过 `git status --porcelain=v1 -z --untracked-files=all` 读取变更。
- 可选：TortoiseGit。默认 Git 动作使用 `TortoiseGitProc.exe`、`explorer.exe`、`notepad.exe`，可通过配置覆盖。

## 构建与运行

```bash
dotnet restore HaloCreek/HaloCreek.csproj
dotnet build HaloCreek/HaloCreek.csproj
dotnet run --project HaloCreek/HaloCreek.csproj
```

发布时可以启用发布图标：

```bash
dotnet publish HaloCreek/HaloCreek.csproj -c Release /p:UsePublishIcon=true
```

## 配置

HaloCreek 支持两层配置：

- 全局配置：`%APPDATA%\HaloCreek\config.json`
- Workspace 配置：`<workspace>/.HaloCreek/config.json`

Workspace 配置会覆盖全局配置；没有配置文件时使用默认配置。配置读取失败当前会回退到默认值，适合 MVP 阶段快速使用，但不会主动修复损坏配置。

示例：

```json
{
  "CodexExecutableName": "codex",
  "CodexLaunchArguments": [],
  "MaxSessionHistoryFiles": 100,
  "GitFileBrowserDoubleClickActionId": "Open",
  "GitFileBrowserActions": [
    {
      "Id": "OpenDiff",
      "Label": "OpenDiff",
      "ShowAsButton": true,
      "Placement": "Left",
      "Executable": "TortoiseGitProc.exe",
      "Arguments": ["/command:diff", "/path:{SelectedPath}"]
    },
    {
      "Id": "Open",
      "Label": "Open",
      "ShowAsButton": false,
      "Placement": "Left",
      "Executable": "explorer.exe",
      "Arguments": ["{SelectedPath}"]
    },
    {
      "Id": "Commit",
      "Label": "Commit",
      "ShowAsButton": true,
      "Placement": "Right",
      "Executable": "TortoiseGitProc.exe",
      "Arguments": ["/command:commit", "/path:{WorkspaceRoot}"]
    }
  ]
}
```

当前 Git action 支持的 token：

- `{WorkspaceRoot}`：当前 workspace 根目录。
- `{SelectedPath}`：当前选中 Git 变更的完整路径。

如果 action 参数使用 `{SelectedPath}`，必须先选中一个 Git 变更。

## Codex 历史

History 页签会尝试读取以下位置的 session 历史：

- `$CODEX_HOME/sessions`
- `~/.codex/sessions`

在 Windows 环境中，HaloCreek 会同时尝试通过 WSL 的环境变量和可读路径候选定位历史文件。历史记录按当前 workspace 过滤，Windows 路径和 WSL `/mnt/<drive>/...` 路径会做等价比较。

## 项目结构

```text
HaloCreek/
  App.axaml(.cs)                  应用入口和对象组装
  ViewModels/                     主窗口、页签和组件 ViewModel
  Views/                          Avalonia XAML 视图
  Services/                       workspace、session、tmux、git、配置、历史等服务
  Infrastructure/                 平台相关能力：WSL、Windows Terminal、剪贴板、弹窗、路径
  Models/                         配置、session、Git 变更等数据模型
  Logging/                        应用内日志
docs/
  DesigningRules.md               设计规则
  roadmap/                        MVP 计划、阶段文档和版本总结
```

## 已知限制

- 当前没有自动化测试项目。
- Ongoing Sessions 只管理当前 HaloCreek 进程创建的 tmux session，不扫描或接管已有 session。
- tmux 状态通过 heartbeat 文件轮询判断，不解析 Codex 内部状态。
- Git 外部动作只判断进程是否成功启动，不判断外部工具是否完成真实业务。
- History 依赖 Codex JSONL 历史格式；格式变化时需要调整 reader。
- History 搜索当前只过滤 Initial Prompt，不做全文检索。

## License

MIT. See [LICENSE](LICENSE).
