# HaloCreek Startup Guide

HaloCreek 是一个面向 Windows + Codex CLI 的本地桌面工作流壳层。当前版本启动后必须有一个有效的 Git 仓库根目录作为 workspace；Codex session 通过 Windows 侧 `codex.exe` 在后台 psmux session 中运行；只有用户显式点击 `CLI` 时才打开前台命令窗口；Diff 和部分 Git 操作依赖 TortoiseGit。

功能操作说明见 [Feature Guide](FeatureGuide.md)。

## 1. 依赖安装与检查

### 1.1 推荐安装入口

在 Windows 侧打开 PowerShell 或命令提示符，进入仓库目录后运行：

```bat
scripts\install_deps.bat
```

脚本会尝试安装或确认以下依赖：

- `psmux.exe`：通过 WinGet 安装 `marlocarlo.psmux`。
- `codex.exe`：通过 Codex CLI Windows 官方安装脚本安装。
- `git.exe`：通过 WinGet 安装 Git for Windows。
- `TortoiseGitProc.exe`：通过 WinGet 安装 TortoiseGit。

Git 和 TortoiseGit 安装器可能显示安装 UI。脚本在安装后会刷新当前进程的 PATH，并在结束前再次验证依赖；如果验证仍失败，请人工检查对应工具是否把可执行文件目录写入了用户或系统 PATH。

离线包由 `scripts\prepare_offline_pack.ps1` 生成。离线包会同时收集 Microsoft Visual C++ Redistributable x64，并在安装 TortoiseGit 前先安装该运行库。`install_offline.ps1` 运行时会在离线包根目录创建 `diagnostics` 目录，保存安装 transcript、环境快照、安装器日志和失败摘要，便于远程诊断 MSI 安装失败。

### 1.2 手动检查项

如果不使用安装脚本，可以手动确认：

```powershell
psmux.exe --version
codex.exe --version
git.exe --version
where.exe TortoiseGitProc.exe
```

期望结果：

- `psmux.exe --version` 能正常输出版本号。
- `codex.exe --version` 输出形如 `codex-cli x.y...`。
- `git.exe --version` 输出形如 `git version x.y...`。
- `where.exe TortoiseGitProc.exe` 能找到 TortoiseGit 的可执行文件路径。

`scripts\check_deps.bat` 当前仍保留旧的 WSL/tmux 检查逻辑，不作为 0.3 Windows/psmux 路线的推荐检查入口。

### 1.3 依赖用途

`psmux.exe` 用于创建、维护和 attach 后台 session。HaloCreek 的 `Launch`、`Resume`、`SendToFront`、`CLI`、`Exit`、`Restart` 都依赖它。

`codex.exe` 是实际执行 Agent 编程任务的 Codex CLI。HaloCreek 不内置 Codex 执行能力，只负责启动、发送 prompt、读取 session 文件和展示状态。

`git.exe` 用于 workspace 校验、Git 修改列表、Review 快照对比、文件恢复和文件补全候选。

`TortoiseGitProc.exe` 用于打开外部 diff、commit 和 log 窗口。

## 2. 启动与 Workspace

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

## 3. 配置说明

### 3.1 配置文件位置与优先级

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
  "MaxSessionHistoryFiles": 100,
  "MarkdownLineJumpCommands": {
    "code": ["--goto", "{Path}:{Line}"],
    "rider64": ["--line", "{Line}", "{Path}"]
  },
  "ShortcutPhraseCategories": [
    {
      "Name": "自定义",
      "Aliases": ["custom"],
      "Description": "自定义快捷语。",
      "Items": [
        {
          "Title": "示例",
          "Aliases": ["sample"],
          "Description": "插入示例文本。",
          "InsertText": "示例 prompt"
        }
      ]
    }
  ],
  "PromptTemplateItems": [
    {
      "Title": "示例模板",
      "Description": "插入示例模板文本。",
      "InsertText": "示例模板正文"
    }
  ]
}
```

当前版本不会在运行中热重载配置。修改配置后，需要重新启动应用，或至少重新选择 workspace 以重新加载 effective config。

### 3.2 默认配置

内置默认值为：

```json
{
  "CodexExecutableName": "codex",
  "CodexLaunchArguments": [],
  "MaxSessionHistoryFiles": 100,
  "MarkdownLineJumpCommands": {
    "devenv": ["/edit", "{Path}", "/command", "Edit.GoTo {Line}"],
    "code": ["--goto", "{Path}:{Line}"],
    "rider64": ["--line", "{Line}", "{Path}"]
  }
}
```

另外内置默认配置包含一组英文 `ShortcutPhraseCategories` 和 `PromptTemplateItems`。发布包会包含 `OptionalConfig\config.zh-CN.json`，当前这个目录只放本地化用的可选配置；需要其他语言时，按同样方式新增对应 locale tag 的配置文件即可。需要中文默认快捷语和模板时，可以复制 `config.zh-CN.json` 的内容到全局或 workspace 配置文件。缺失字段会继承低优先级配置或默认值。`CodexExecutableName` 为空白时会被忽略；`MaxSessionHistoryFiles` 必须大于 0，否则会继承低优先级配置或默认值。

### 3.3 Codex 启动配置

`CodexExecutableName` 控制 HaloCreek 在 psmux session 内启动的 Codex 可执行文件名。默认值是 `codex`。

关联功能：

- Prompt Editor 的 `Launch`。
- History 的 `Resume`。
- Ongoing Sessions 的 `Restart`。

`CodexLaunchArguments` 会追加到 Codex 可执行文件之后、具体 prompt 或 `resume <sessionId>` 之前。

例如：

```json
{
  "CodexExecutableName": "codex",
  "CodexLaunchArguments": ["--model", "gpt-5-codex"]
}
```

Prompt Editor 的 `Launch` 最终类似：

```powershell
codex --model gpt-5-codex "<prompt>"
```

History 的 `Resume` 最终类似：

```powershell
codex --model gpt-5-codex resume <sessionId>
```

这些命令在 Windows 侧 psmux session 中执行，所以可执行文件和相关认证状态必须存在于 Windows 环境中。

### 3.4 History 配置

`MaxSessionHistoryFiles` 控制 History 每次从 Codex sessions 目录中最多读取多少个最新的 `*.jsonl` 文件。默认值是 `100`。

关联功能：

- History 列表展示。
- History 搜索。
- History `Resume` 的可见候选范围。
- Prompt 模板菜单中的 `Recent Initial Prompts` 候选范围。

如果 Codex 历史很多，可以调小这个值以减少读取时间；如果需要回看更早的 session，可以调大这个值。

示例：

```json
{
  "MaxSessionHistoryFiles": 300
}
```

### 3.5 Markdown 行号跳转配置

`MarkdownLineJumpCommands` 控制 session 消息中本地文件链接带行号时如何打开编辑器。key 是可执行文件名，value 是参数模板数组。

支持的占位符：

- `{Path}`：解析后的本地文件路径。
- `{Line}`：链接中的行号。

内置默认值支持 Visual Studio、VS Code 和 JetBrains Rider：

```json
{
  "MarkdownLineJumpCommands": {
    "devenv": ["/edit", "{Path}", "/command", "Edit.GoTo {Line}"],
    "code": ["--goto", "{Path}:{Line}"],
    "rider64": ["--line", "{Line}", "{Path}"]
  }
}
```

合并规则是按 key 覆盖或追加，不会删除低优先级配置中未提到的命令。命令参数只允许使用 `{Path}` 和 `{Line}` 两个占位符；包含其他 `{...}` 占位符的条目会被忽略。

### 3.6 快捷语配置

`ShortcutPhraseCategories` 控制 `#` 补全里的静态快捷语。全局配置或 workspace 配置只要提供该字段，就会整体替换低优先级的快捷语列表，不做按类别或条目的深度合并。

类别字段：

- `Name`：类别名称。
- `Aliases`：类别别名，query 精确匹配时直接展示该类别下的条目。
- `Description`：类别说明。
- `Items`：类别下的快捷语条目。

条目字段：

- `Title`：条目展示名称。
- `Aliases`：条目搜索关键字。
- `Description`：条目说明。
- `InsertText`：接受条目后插入 prompt 的文本。

### 3.7 Prompt 模板配置

`PromptTemplateItems` 控制 Prompt 输入框右键菜单里的 `Templates` 内置模板。全局配置或 workspace 配置只要提供该字段，就会整体替换低优先级的模板列表，不做按条目的深度合并。`Recent Initial Prompts` 仍来自 Codex session history，不受该字段影响。

模板字段：

- `Title`：模板展示名称。
- `Description`：模板说明和预览提示。
- `InsertText`：点击模板后插入 prompt 的文本。

### 3.8 当前不可配置项

以下行为当前是内置实现，不通过 `config.json` 配置：

- Modified 列表的右键动作集合和双击默认动作。
- Snapshot 自动刷新间隔和文件系统监听 debounce。
- Session keep-alive 探测频率，当前为 3 秒。
- ClipLocate 的候选文件大小上限，当前为 2 MB；该能力当前没有业务 UI 入口消费。
- `CLI` 前台窗口启动方式，当前固定为直接启动对应 attach 命令。
- 后台 session 管理程序，当前固定调用 `psmux.exe`。
- 外部 diff 程序，当前固定调用 `TortoiseGitProc.exe`。

这些项如果后续需要调整，应先在实现中增加明确的配置字段，再更新本文档。
