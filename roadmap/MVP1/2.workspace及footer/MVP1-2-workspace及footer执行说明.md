# MVP1 阶段 2：workspace 及 footer 执行说明

## 1. 目标

阶段 2 只实现 workspace 选择与分发闭环：

- Footer 点击按钮后弹出目录选择。
- 选择结果经过路径规范化和校验。
- 校验通过后，Footer 通过 `Action<string>` 回调通知上层切换 workspace。
- `MainWindowViewModel.SetWorkspacePath(...)` 继续作为唯一分发入口，把 workspace path 同步给 4 个 tab 和 Footer。
- Footer 显示当前 workspace 和简短状态。

本阶段不实现 workspace service，也不引入 workspace 业务模型。workspace 仍然只是工作目录路径字符串。

## 2. 已确定技术决策

- 不单独实现 `WorkspaceService`。
- 新增 `PlatformInfrastructure`，负责平台相关能力，包括目录选择、路径规范化、路径校验等。
- `PlatformInfrastructure` 不表达 workspace 业务语义，只处理底层平台和路径能力。
- `PlatformInfrastructure` 持有主窗口 `Window`，目录选择通过 Avalonia 新版 `StorageProvider.OpenFolderPickerAsync` 实现。
- `WorkspaceFooterViewModel` 持有 `Action<string> setWorkspace`，选择并校验成功后调用它分发 workspace 变化。
- `MainWindowViewModel` 继续持有当前 workspace，并通过 `SetWorkspacePath(string workspacePath)` 向各 tab 和 Footer 分发。
- workspace 路径只接受已存在目录。
- Footer 状态文案阶段 2 先沿用英文。

## 3. 建议实现

### 3.1 新增 PlatformInfrastructure

新增文件：

```text
HaloCreek/Infrastructure/PlatformInfrastructure.cs
```

建议先提供 3 个能力：

```csharp
public sealed class PlatformInfrastructure
{
    public PlatformInfrastructure(Window owner);

    public Task<string?> SelectDirectoryAsync();

    public string NormalizeDirectoryPath(string path);

    public bool IsValidDirectoryPath(string path);
}
```

实现原则：

- `SelectDirectoryAsync(...)` 使用 `owner.StorageProvider.OpenFolderPickerAsync(...)`，返回用户选择的原始路径或 `null`。
- `NormalizeDirectoryPath(...)` 使用 .NET 路径 API 转为完整路径，并去掉无意义空白。
- `IsValidDirectoryPath(...)` 至少检查非空、路径格式有效、目录存在；不存在的目录不作为有效 workspace。
- 路径校验失败时不抛到 UI 层；Footer 显示可读状态。
- `Window` 只作为平台 UI 上下文保存在 `PlatformInfrastructure`，不要传入业务 ViewModel。

### 3.2 调整 WorkspaceFooterViewModel

当前 `ChooseWorkspaceCommand` 只是占位。阶段 2 改成真实流程：

```text
ChooseWorkspaceCommand
└─ 选择目录
   ├─ 用户取消：保持当前 workspace 不变，状态回到 Ready
   ├─ 校验失败：保持当前 workspace 不变，Footer 显示错误状态
   └─ 校验成功：调用 setWorkspace(normalizedPath)，Footer 显示 Ready
```

`WorkspaceFooterViewModel` 构造函数建议调整为接收：

```csharp
PlatformInfrastructure platformInfrastructure
```

Footer 另外提供一次性设置分发入口：

```csharp
public void SetWorkspaceDispatcher(Action<string> setWorkspace)
```

Footer 不持有 `Window`。目录选择需要的窗口上下文由 `PlatformInfrastructure` 内部持有。

### 3.3 由 MainWindowViewModel 设置 Footer 分发入口

`MainWindowViewModel` 本来就持有 `WorkspaceFooterViewModel`，因此 workspace 分发入口也由 `MainWindowViewModel` 塞给 Footer。`App` 只负责创建对象和注入依赖，不需要绕一层处理 workspace 切换。

`MainWindowViewModel` 构造函数中增加：

```csharp
WorkspaceFooter.SetWorkspaceDispatcher(SetWorkspacePath);
```

组装关系保持为：

```text
创建 MainWindow
创建 tab viewModels
创建 PlatformInfrastructure(mainWindow)
创建 WorkspaceFooterViewModel(platformInfrastructure)
创建 MainWindowViewModel(..., workspaceFooter)
MainWindowViewModel 构造时调用 workspaceFooter.SetWorkspaceDispatcher(SetWorkspacePath)
```

`WorkspaceFooterViewModel` 在 dispatcher 未设置时，点击选择按钮应给出明确状态，例如 `Workspace dispatcher is not connected`，避免空引用或静默失败。

### 3.4 MainWindowViewModel 保持现有职责

`MainWindowViewModel.SetWorkspacePath(...)` 只做：

- 参数防御。
- 更新 `CurrentWorkspacePath`。
- 调用 4 个 tab 的 `SetWorkspacePath(...)`。
- 调用 `WorkspaceFooter.SetWorkspacePath(...)`。

不要把路径选择、路径校验、目录存在性检查放进 `MainWindowViewModel`。

## 4. 任务拆分

- [ ] **2-T01 新增 PlatformInfrastructure**：持有主窗口 `Window`，通过 `StorageProvider.OpenFolderPickerAsync` 提供目录选择，并提供路径规范化、已存在目录校验能力。
- [ ] **2-T02 接通 Footer 选择命令**：点击按钮后打开目录选择，处理取消、失败、成功状态。
- [ ] **2-T03 接通 setWorkspace 回调**：Footer 校验成功后调用 `Action<string>`，由 `MainWindowViewModel.SetWorkspacePath(...)` 统一分发。
- [ ] **2-T04 组装依赖**：在 `App` 中先创建 `MainWindow`，再创建 `PlatformInfrastructure(mainWindow)` 和 Footer；由 `MainWindowViewModel` 构造时调用 `WorkspaceFooter.SetWorkspaceDispatcher(SetWorkspacePath)`。
- [ ] **2-T05 人工验收**：启动窗口，确认选择 workspace 后 Footer 显示路径，4 个 tab 收到同一路径；取消选择不改变当前路径。

## 5. 决策点

- 目录选择直接使用 Avalonia 新版 `StorageProvider.OpenFolderPickerAsync`。
- `Window` 作为全局 UI context，由 `PlatformInfrastructure` 持有；业务 ViewModel 不持有 `Window`。
- workspace 路径只接受已存在目录，不支持输入或选择未来创建的目录。
- Footer 状态文案阶段 2 先使用英文，保持与当前 UI 一致。

## 6. 验收标准

- 初始状态显示 `No workspace selected`。
- 点击 `Select Workspace` 可以打开目录选择窗口。
- 取消选择不会改变当前 workspace。
- 选择不存在或无效路径时不会分发到 tab，Footer 显示失败状态。
- 选择有效目录后，Footer 显示规范化后的完整路径。
- `PromptEditorViewModel`、`HistorySessionsViewModel`、`OngoingSessionsViewModel`、`GitViewModel` 收到同一个 workspace path。
