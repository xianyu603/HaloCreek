# MVP1 阶段 1：界面布局方案

## 目标

本阶段只完成界面与工程骨架决策，不实现各功能的真实业务逻辑。

- 敲定主窗口的 `Tab + Footer` 基本结构。
- 为 4 个核心页签预留独立视图与 ViewModel。
- 敲定 MVP1 期间可持续演进的工程目录结构。
- 明确主要技术决策，避免后续阶段频繁重排 UI 和模块边界。

## 主界面布局

主窗口建议采用上下结构：

```text
MainWindow
├─ 主内容区：TabControl
│  ├─ Prompt Editor
│  ├─ History Sessions
│  ├─ Ongoing Sessions
│  └─ Git
└─ Footer：当前 workspace 摘要 + workspace 选择按钮 + 状态提示
```

### Tab 规划

- `Prompt Editor`：提示词编辑区、文件拖入区域、Launch 按钮。
- `History Sessions`：当前 workspace 的历史 session 列表、搜索、resume、reedit init prompt。
- `Ongoing Sessions`：正在运行的 session 列表、拉到前台、跳转到历史记录。
- `Git`：当前 diff 文件列表、打开 diff 工具、commit 入口。

### Footer 规划

- 左侧显示当前 workspace 路径。
- 中间显示简短状态，如 `Ready`、`Loading sessions...`、`Launching...`。
- 右侧提供 workspace 选择按钮。
- MVP1 阶段 2 再接入真实 workspace 切换；阶段 1 只保留布局和绑定字段。

## 建议工程结构

```text
HaloCreek/
├─ Views/
│  ├─ MainWindow.axaml
│  ├─ Tabs/
│  │  ├─ PromptEditorView.axaml
│  │  ├─ HistorySessionsView.axaml
│  │  ├─ OngoingSessionsView.axaml
│  │  └─ GitView.axaml
│  └─ Components/
│     └─ WorkspaceFooterView.axaml
├─ ViewModels/
│  ├─ MainWindowViewModel.cs
│  ├─ Tabs/
│  │  ├─ PromptEditorViewModel.cs
│  │  ├─ HistorySessionsViewModel.cs
│  │  ├─ OngoingSessionsViewModel.cs
│  │  └─ GitViewModel.cs
│  └─ Components/
│     └─ WorkspaceFooterViewModel.cs
├─ Models/
│  ├─ WorkspaceInfo.cs
│  ├─ SessionInfo.cs
│  ├─ OngoingSessionInfo.cs
│  └─ GitChangeInfo.cs
├─ Services/
│  ├─ WorkspaceService.cs
│  ├─ SessionHistoryService.cs
│  ├─ OngoingSessionService.cs
│  ├─ GitService.cs
│  └─ ConfigService.cs
└─ Infrastructure/
   ├─ ProcessRunner.cs
   └─ FileSystem.cs
```

阶段 1 不必一次创建全部服务实现，但建议先按这个边界组织文件，后续阶段逐步填充。

## 主要技术决策点

| 问题 | 可选方案 | 建议 |
| --- | --- | --- |
| 主窗口使用什么布局容器？ | `Grid` 上下两行；`DockPanel`；嵌套 `StackPanel` | 使用 `Grid`：第一行 `*` 放 `TabControl`，第二行 `Auto` 放 Footer。结构稳定，后续 footer 高度变化不会影响主内容逻辑。 |
| 页签使用 Avalonia 原生 `TabControl` 还是自定义导航？ | 原生 `TabControl`；左侧导航 + ContentControl；自定义按钮栏 | MVP1 使用原生 `TabControl`。实现成本低，足够验证多页签工作流；后续需要更复杂导航时再替换。 |
| 每个 tab 是否拆成独立 View/UserControl？ | 全部写在 `MainWindow.axaml`；每个 tab 一个 `UserControl`；只拆 ViewModel | 每个 tab 拆成独立 `UserControl + ViewModel`。阶段 3-6 可以独立推进，避免 `MainWindow` 变成大文件。 |
| ViewModel 如何组织？ | 一个 `MainWindowViewModel` 管所有状态；每个 tab 独立 VM；引入完整 DI 容器 | 使用每个 tab 独立 VM，`MainWindowViewModel` 只负责组合。暂不引入 DI 容器，先手动组装，等服务复杂后再评估。 |
| Footer 的状态归属在哪里？ | 放在 `MainWindowViewModel`；独立 `WorkspaceFooterViewModel`；每个 tab 各自显示 | 独立 `WorkspaceFooterViewModel`，由 `MainWindowViewModel` 持有。workspace 是跨 tab 状态，独立后更容易在阶段 2 接入切换逻辑。 |
| workspace 状态如何流向各 tab？ | tab 直接读全局变量；Main VM 分发；事件总线；服务查询 | MVP1 使用 Main VM 持有当前 workspace，并在创建/切换时传给各 tab VM。暂不使用事件总线，降低隐式依赖。 |
| 设计期/占位数据怎么处理？ | 不做占位；写死在 XAML；ViewModel 提供 mock 数据 | 阶段 1 使用 ViewModel 内的 mock 数据，方便直接看布局效果。阶段 7 Config 化前可以继续硬编码。 |
| 命令绑定用什么方式？ | code-behind 事件；CommunityToolkit `RelayCommand`；ReactiveUI | 使用 CommunityToolkit `RelayCommand`。项目已引用 `CommunityToolkit.Mvvm`，和现有 MVVM 方向一致。 |
| 文件拖入能力放在哪里？ | MainWindow 统一处理；PromptEditorView 自己处理；独立 DragDropService | 阶段 3 在 `PromptEditorView` 内处理拖入即可。拖入只影响提示词编辑器，不需要提前抽公共服务。 |
| 历史 session 解析模块现在是否做格式防御？ | 完整兼容抽象；只支持当前 Codex 格式；直接 UI 里解析文件 | 只支持当前 Codex session 格式，但解析逻辑必须放在 `SessionHistoryService`，UI 只消费 `SessionInfo`。未来格式变化时只替换服务。 |
| ongoing session 是否现在接 tmux？ | 阶段 1 就接；阶段 5 再接；完全跳过 | 阶段 1 只保留列表布局和模型字段，阶段 5 再验证 tmux。避免布局阶段被外部进程控制复杂度拖住。 |
| Git tab 是否现在接真实 git？ | 阶段 1 就跑 git；只做占位列表；直接调用 TortoiseGit | 阶段 1 只做占位列表与按钮。阶段 6 再接 `GitService` 和 TortoiseGitMerge/commit。 |
| 配置系统是否现在实现？ | 先实现 global/workspace config；硬编码到阶段 7；先写接口不实现 | 阶段 1 可以预留 `ConfigService` 文件边界，但真实读取写入延后到阶段 7。当前阶段先硬编码默认值。 |
| 是否引入第三方 UI 控件库？ | 只用 Avalonia + Fluent；引入完整控件库；自绘控件 | MVP1 只用 Avalonia 原生控件和 Fluent 主题。当前界面偏工具型，原生控件足够，减少依赖风险。 |
| 是否做响应式布局？ | 固定窗口；简单最小尺寸；完整自适应 | 设置合理最小窗口尺寸，tab 内使用 `Grid`/`DockPanel` 支持基本拉伸。MVP1 不做复杂响应式。 |
| 工程是否立即加入单元测试项目？ | 阶段 1 加测试项目；后续需要时加；不测试 | 阶段 1 暂不加测试项目。等 `SessionHistoryService`、`GitService` 等有真实逻辑后，再优先给服务层补测试。 |

## 阶段 1 交付物

- `MainWindow` 改为稳定的 `TabControl + Footer` 布局。
- 4 个 tab 的 View/UserControl 和 ViewModel 占位。
- Footer 的 View/UserControl 和 ViewModel 占位。
- `Models` 目录补充 MVP1 需要的基础数据模型占位。
- 不接真实 Codex、tmux、git、配置读写逻辑。

## 后续阶段衔接

- 阶段 2：实现 workspace 选择，Footer 显示真实当前 workspace。
- 阶段 3：实现 Prompt Editor 的编辑、拖入文件路径、Launch。
- 阶段 4：实现当前 workspace 的历史 session 解析与操作。
- 阶段 5：验证 ongoing session/tmux 管理。
- 阶段 6：实现 git diff 列表和外部 diff/commit 工具接入。
- 阶段 7：把硬编码参数收敛到 global/workspace 分层 JSON 配置。
