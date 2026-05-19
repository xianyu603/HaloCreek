# MVP1 阶段 3：Prompt Editor Tab 执行说明

## 1. 目标

阶段 3 实现 Prompt Editor 页签的最小可用闭环：

- 用户可以在编辑区输入多行 prompt。
- 用户拖入文件或目录后，编辑区自动追加对应路径文本。
- 点击 `Launch` 后，通过 `SessionLifecycleService` 启动一次真实 Codex session。
- Launch 前使用当前 workspace path 和有效配置。
- Footer 全局状态能展示启动成功、失败、缺少 workspace、缺少 prompt 等结果。

本阶段只做 prompt 编辑、路径追加和 launch 入口，不实现历史 session 解析、ongoing session 列表同步、复杂 prompt 模板管理或配置编辑器。

## 2. 当前基础

已有代码边界：

- `PromptEditorView.axaml`：已有多行 `TextBox` 和禁用态 `Launch` 按钮占位。
- `PromptEditorViewModel`：已有 `PromptText`、`WorkspacePath`、`GetPrimaryDroppedPath(...)`、`LaunchPrompt()`。
- `DragDropService`：当前只返回第一个有效拖入路径。
- `SessionLifecycleService.Launch(...)`：已有方法签名，但当前返回未接通状态。
- `ConfigService.LoadEffectiveConfig(...)`：当前返回 MVP1 默认配置。
- `MainWindowViewModel.SetWorkspacePath(...)`：已经把 workspace path 分发到 `PromptEditorViewModel`。

阶段 3 优先沿用这些边界，避免新增只服务单个按钮的薄抽象。

## 3. 建议实现

### 3.1 接通 View 绑定

- `TextBox.Text` 双向绑定到 `PromptEditorViewModel.PromptText`。
- `Launch` 按钮绑定到 ViewModel 命令，例如 `LaunchCommand`。
- `Launch` 按钮的可用状态由 ViewModel 计算：必须有 workspace，且 prompt 非空。
- Launch 结果同步到 Footer 全局状态；Prompt Editor 页内不单独新增结果状态展示。

### 3.2 处理拖入路径

在 `PromptEditorView.axaml.cs` 中处理 Avalonia 拖放事件：

- 从拖放数据中读取文件/目录路径。
- 调用 `PromptEditorViewModel.GetPrimaryDroppedPath(...)` 做 MVP1 统一筛选。
- 将路径追加到 `PromptText`，而不是直接操作 `TextBox.Text`。
- 追加格式建议先使用纯路径文本，每个路径单独占一行。

阶段 3 只接入第一个拖入路径，文件和目录一视同仁，不做目录展开。

### 3.3 实现 Launch 命令

`PromptEditorViewModel` 新增异步或同步命令：

```text
LaunchCommand
├─ 校验 workspace path
├─ 校验 prompt 非空
├─ 读取有效配置
├─ 调用 SessionLifecycleService.Launch(...)
└─ 更新 Footer 全局状态
```

阶段 3 需要接入真实启动。真实进程启动逻辑必须收敛在 `SessionLifecycleService` 内，ViewModel 只负责校验、调用服务和分发状态。

### 3.4 接入真实 session 启动

阶段 3 的真实 launch 实现原则：

- `SessionLifecycleService` 负责组装进程启动参数和工作目录。
- 通过 `Process.Start` 启动真实 `codex`，但 `PromptEditorViewModel` 不直接调用 `Process.Start`。
- codex 可执行文件和固定参数使用 `AppConfig.DefaultForMvp1`。
- prompt 文本暂定通过命令行参数传给 codex。
- 启动时拉起 Terminal，让用户可以看到并接管 session。
- 失败时返回 `SessionLaunchResult`，不要把异常直接抛到 UI。

## 4. 任务拆分

按风险优先执行，先验证真实 Codex 启动链路，再逐步接 UI 输入：

- [ ] **3-T01 实现真实 session 启动**：在 `SessionLifecycleService` 内通过 `Process.Start` 拉起 Terminal，并通过命令行参数传递 prompt 启动真实 `codex`。先用固定 prompt 验证进程启动、工作目录、Terminal 拉起和错误返回。
- [ ] **3-T02 新增 LaunchCommand 与状态分发**：在 ViewModel 中提供命令和可用状态，先以固定 prompt 调用 `SessionLifecycleService`，并把 launch 结果同步到 Footer 全局状态。
- [ ] **3-T03 接通 Launch 按钮**：按钮绑定命令，先完成固定 prompt 的手动点击启动闭环。
- [ ] **3-T04 接通 PromptText 绑定**：让编辑区与 `PromptEditorViewModel.PromptText` 双向同步，并把 LaunchCommand 从固定 prompt 切换为使用 `PromptText`。
- [ ] **3-T05 接通拖放事件**：从 Avalonia 拖放数据读取路径，调用 `DragDropService`，把第一个路径以裸路径形式追加到 `PromptText`。
- [ ] **3-T06 人工验收**：输入 prompt、拖入路径、未选择 workspace 点击/禁用、选择 workspace 后 launch 结果展示。

## 5. 已确定决策

- **Launch 接入真实进程**：阶段 3 需要启动真实 `codex`。
- **prompt 传递方式**：暂定通过命令行参数传递 prompt。
- **启动实现边界**：直接使用 `Process.Start`，但必须隔离在 `SessionLifecycleService` 内。
- **启动后窗口行为**：拉起 Terminal。
- **拖入路径格式**：追加裸路径。
- **多文件拖入策略**：只取第一个路径。
- **路径类型处理**：文件和目录一视同仁，不做目录展开。
- **状态展示位置**：launch 结果同步到 Footer 的全局状态。
- **Launch 后 prompt 状态**：启动成功后保留当前 prompt。
- **重复点击与并发启动**：允许同一 workspace 下同时启动多个 session。
- **配置依赖范围**：阶段 3 使用 `AppConfig.DefaultForMvp1`。
- **错误文案语言**：继续使用英文。

## 6. 验收标准

- Prompt Editor 编辑区可以正常输入和换行。
- 选择 workspace 后，Prompt Editor 能收到同一个 workspace path。
- 拖入一个文件或目录后，路径会追加到 prompt 文本中。
- prompt 为空或 workspace 缺失时不能无声 launch，必须禁用按钮或展示明确状态。
- 点击 `Launch` 后，Footer 展示 `SessionLaunchResult.StatusMessage`。
- `PromptEditorViewModel` 不直接依赖 Avalonia 控件或 `Process` 细节。
- `SessionLifecycleService` 仍是 session 启动能力的唯一服务入口。
