# MVP1 阶段 1：界面布局方案

## 1. 目标

本阶段只完成界面与工程骨架决策，不实现各功能的真实业务逻辑。

- 敲定主窗口的 `Tab + Footer` 基本结构。
- 为 4 个核心页签预留独立视图与 ViewModel。
- 敲定 MVP1 期间可持续演进的工程目录结构。
- 明确主要技术决策，避免后续阶段频繁重排 UI 和模块边界。
- 固化跨页面服务、外部格式读取、占位实现和应用启动组装的边界。

## 2. 主界面布局

主窗口采用上下结构：

```text
MainWindow
├─ 主内容区：TabControl
│  ├─ Prompt Editor
│  ├─ History Sessions
│  ├─ Ongoing Sessions
│  └─ Git
└─ Footer：当前 workspace 摘要 + workspace 选择按钮 + 状态提示
```

### 2.1 Tab 规划

- `Prompt Editor`：提示词编辑区、文件拖入区域、Launch 按钮。
- `History Sessions`：当前 workspace 的历史 session 列表、搜索、resume、reedit init prompt。
- `Ongoing Sessions`：正在运行的 session 列表、拉到前台、跳转到历史记录。
- `Git`：当前 diff 文件列表、打开 diff 工具、commit 入口。

### 2.2 Footer 规划

- 左侧显示当前 workspace 路径。
- 中间显示简短状态，如 `Ready`、`Loading sessions...`、`Launching...`。
- 右侧提供 workspace 选择按钮。
- MVP1 阶段 2 再接入真实 workspace 切换；阶段 1 只保留布局和绑定字段。

## 3. 工程结构

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
│  ├─ GitChangeInfo.cs
│  └─ AppConfig.cs
├─ Services/
│  ├─ WorkspaceService.cs
│  ├─ OngoingSessionService.cs
│  ├─ GitService.cs
│  ├─ ConfigService.cs
│  ├─ DragDropService.cs
│  ├─ SessionLaunchService.cs
│  └─ SessionHistory/
│  │  ├─ SessionHistoryService.cs
│  │  ├─ ISessionHistoryReader.cs
│  │  ├─ MockHistorySessionReader.cs
│  │  └─ CodexSessionHistoryReader.cs
└─ Infrastructure/
   ├─ ProcessRunner.cs
   └─ FileSystem.cs
```

阶段 1 按这个边界组织文件。真实业务实现可以逐步填充，但上层接口、ViewModel 依赖关系和服务分层需要从一开始保持正式形态。

`Services/` 默认保持平铺，只有内部文件会继续增长、且需要隔离外部数据读取边界的模块才拆子目录。阶段 1 只将历史 session 相关文件放入 `Services/SessionHistory/`，其他服务仍直接放在 `Services/` 下。

窗口和页面布局由 XAML 静态声明；`App.OnFrameworkInitializationCompleted()` 只负责创建服务、创建 ViewModel、注入依赖，并把组装好的 `MainWindowViewModel` 设置为 `MainWindow.DataContext`。View 不在 App 中手动拼装。

占位数据由服务层或底层 reader 的 mock 实现返回，不写入 XAML，也不直接写在 ViewModel 里。ViewModel 始终依赖正式服务接口和正式数据模型，后续替换真实实现时不改变 ViewModel 的调用方式。例如：

```text
HistorySessionsViewModel
└─ Services/SessionHistory/SessionHistoryService
   └─ Services/SessionHistory/ISessionHistoryReader
      └─ MockHistorySessionReader / CodexSessionHistoryReader
```

`ConfigService` 在阶段 1 使用正式方法签名返回默认配置：

```csharp
public sealed class ConfigService
{
    public AppConfig LoadEffectiveConfig(WorkspaceInfo? workspace)
    {
        return AppConfig.DefaultForMvp1;
    }
}
```

## 4. 主要技术结论

- 主窗口使用 `Grid` 上下两行布局：第一行 `*` 放 `TabControl`，第二行 `Auto` 放 Footer。该结构固定为主窗口基础布局。
- 页签使用 Avalonia 原生 `TabControl`。MVP1 的多页工作流直接建立在原生控件上。
- 每个 tab 拆成独立 `UserControl + ViewModel`。`MainWindow.axaml` 只负责承载主布局和组合各 tab。
- ViewModel 按页面拆分，`MainWindowViewModel` 只负责组合和跨页面状态分发，不承载各 tab 的业务状态。
- Footer 使用独立 `WorkspaceFooterViewModel`，由 `MainWindowViewModel` 持有。workspace 摘要、状态文本和 workspace 选择入口统一归 Footer 组件管理。
- workspace 状态由 `MainWindowViewModel` 持有，并在创建或切换时传递给各 tab ViewModel。各 tab ViewModel 暴露 `SetWorkspace(WorkspaceInfo workspace)` 作为统一的 workspace 更新入口。项目不使用全局变量或事件总线传递 workspace。
- 占位数据通过正式服务边界提供。服务可以临时返回 mock 数据，但 ViewModel、View 和模型结构保持与真实实现一致。
- 命令绑定使用 CommunityToolkit `RelayCommand`。项目继续沿用 `CommunityToolkit.Mvvm` 的 MVVM 写法。
- 文件拖入能力抽成 `Services/DragDropService`。Prompt Editor、History Sessions、Ongoing Sessions 等页面需要处理文件或路径拖入时复用同一服务。
- Prompt Launch 能力抽成 `Services/SessionLaunchService`。该服务负责把提示词、workspace 和固定启动参数转换成一次 Codex 启动请求，并通过 `ProcessRunner` 执行。该边界不需要像历史 session 读取那样再做 reader 层隔离，Codex 启动命令相对稳定，阶段 3 先保持一层服务封装。
- 历史 session 的业务入口固定为 `Services/SessionHistory/SessionHistoryService`。该服务负责历史 session 列表所需信息、搜索、排序、过滤、映射和后续操作准备。
- 历史 session 原始数据读取固定隔离在 `Services/SessionHistory/ISessionHistoryReader` / `CodexSessionHistoryReader` 后面。该层知道文件路径和当前接入来源的文件格式，并把不稳定的原始格式转换成稳定的中间数据。
- UI 只消费稳定模型，例如 `SessionInfo`、`OngoingSessionInfo`、`GitChangeInfo`，不直接解析外部文件或进程输出。
- MVP1 只使用 Avalonia 原生控件和 Fluent 主题，不引入第三方 UI 控件库。
- 窗口设置合理最小尺寸，tab 内使用 `Grid` / `DockPanel` 保证基本缩放体验。本项目不做复杂响应式布局。
- 阶段 1 不新增单元测试项目。服务层出现真实解析、文件系统、进程或 Git 逻辑后，再优先为服务层补测试。

## 5. 任务拆分

- [x] **1-1-T01 主窗口布局骨架**：将 `MainWindow` 调整为 `Grid` 上下两行布局，上方承载 `TabControl`，下方承载 Footer，并设置合理最小窗口尺寸。审阅重点是主布局稳定、缩放不破坏基本结构。
- [x] **1-1-T02 Tab 视图占位**：新增 `Views/Tabs/PromptEditorView.axaml`、`HistorySessionsView.axaml`、`OngoingSessionsView.axaml`、`GitView.axaml`，并在 `MainWindow.axaml` 中以静态 XAML 组合到 4 个 tab。审阅重点是 tab 命名、占位区域和主窗口组合方式。
- [ ] **1-1-T03 Tab ViewModel 占位**：新增 4 个 tab 对应 ViewModel，并让每个 tab ViewModel 暴露统一的 `SetWorkspace(WorkspaceInfo workspace)` 入口。审阅重点是页面状态按 ViewModel 独立持有，`MainWindowViewModel` 不承载 tab 业务状态。
- [x] **1-1-T04 Footer 组件占位**：新增 `WorkspaceFooterView` 和 `WorkspaceFooterViewModel`，提供 workspace 路径、状态文本和 workspace 选择命令绑定字段。审阅重点是 Footer 职责独立，后续阶段可直接接入真实 workspace 切换。
- [ ] **1-1-T05 基础模型占位**：在 `Models` 下新增 `WorkspaceInfo`、`SessionInfo`、`OngoingSessionInfo`、`GitChangeInfo`、`AppConfig`。审阅重点是 UI 和 ViewModel 只依赖稳定模型，不直接暴露外部文件或进程输出格式。
- [ ] **1-1-T06 平铺服务边界占位**：新增 `WorkspaceService`、`OngoingSessionService`、`GitService`、`ConfigService`、`DragDropService`、`SessionLaunchService`。审阅重点是服务方法签名面向后续真实实现，阶段 1 可以返回默认值或 mock 数据。
- [ ] **1-1-T07 历史 session 读取边界占位**：新增 `Services/SessionHistory/SessionHistoryService`、`ISessionHistoryReader`、`MockHistorySessionReader`、`CodexSessionHistoryReader`。审阅重点是原始数据读取和格式解析被隔离在 reader 层，ViewModel 只消费 `SessionInfo`。
- [ ] **1-1-T08 基础设施占位**：新增 `Infrastructure/ProcessRunner` 和 `FileSystem`，供后续 session launch、Git、文件读取等服务复用。审阅重点是只建立边界，不在阶段 1 实现复杂业务逻辑。
- [ ] **1-1-T09 应用启动组装**：在 `App.OnFrameworkInitializationCompleted()` 中创建服务、创建各 ViewModel、注入依赖、组装 `MainWindowViewModel`，并设置 `MainWindow.DataContext`。审阅重点是 App 只做依赖组装，不手动拼装 View。
- [ ] **1-1-T10 构建与人工验收**：完成阶段 1-1 后执行项目构建，并人工检查窗口能启动、4 个 tab 可切换、Footer 字段可显示。审阅重点是骨架可运行，且没有引入真实业务逻辑或额外 UI 控件库。

## 6. 阶段 1 交付物

- `MainWindow` 改为稳定的 `TabControl + Footer` 布局。
- 4 个 tab 的 View/UserControl 和 ViewModel 占位。
- Footer 的 View/UserControl 和 ViewModel 占位。
- `Models` 目录补充 MVP1 需要的基础数据模型占位。
- `Services` 目录补充 workspace、ongoing、git、config、drag-drop、session launch 的平铺服务边界，并将历史 session 相关文件放入 `Services/SessionHistory/`。
- `Services/SessionHistory/ISessionHistoryReader` / `MockHistorySessionReader` / `CodexSessionHistoryReader` 边界占位，用于隔离历史 session 原始数据来源和文件格式。
- `App.OnFrameworkInitializationCompleted()` 完成服务和 ViewModel 的集中组装，并设置 `MainWindow.DataContext`。窗口和页面布局仍由 XAML 静态声明。

## 7. 后续阶段衔接

- 阶段 2：实现 workspace 选择，Footer 显示真实当前 workspace。
- 阶段 3：实现 Prompt Editor 的编辑、拖入文件路径、Launch。
- 阶段 4：实现当前 workspace 的历史 session 解析与操作。
- 阶段 5：实现 ongoing session 管理页面的真实数据来源和操作。
- 阶段 6：实现 Git 页面的真实 diff 列表和提交相关操作。
- 阶段 7：把硬编码参数收敛到 global/workspace 分层配置。
