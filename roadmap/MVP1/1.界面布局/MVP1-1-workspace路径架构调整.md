# MVP1 阶段 1：workspace 路径架构调整

## 改了什么

- 删除 `Models/WorkspaceInfo.cs`。
- 删除 `Services/WorkspaceService.cs`。
- MVP1 阶段把 workspace 明确收敛为工作目录路径字符串，不再建模为独立业务对象。
- `MainWindowViewModel` 改为持有 `CurrentWorkspacePath`，并通过 `SetWorkspacePath(string workspacePath)` 向 4 个 tab 和 Footer 分发。
- 各 tab ViewModel、`ConfigService`、`GitService`、`SessionLifecycleService`、历史 session reader/service 的 workspace 参数统一改为 `workspacePath`。
- `WorkspaceFooterViewModel` 不再依赖 workspace service，只负责显示路径、状态和保留选择命令占位。

## 为什么改

阶段 1 和 MVP1 当前需求里，workspace 只等价于“用户选择的工作目录”。原来的 `WorkspaceInfo` 只有一个 `Path` 字段，`WorkspaceService` 也只做路径 trim、当前值保存和读取，没有真实项目业务语义。

继续保留这两个抽象会让后续阶段误以为 workspace 已经是领域对象或项目服务，容易把路径校验、当前运行态、项目业务语义混在同一层。现在删掉它们，可以让边界更清楚：

- 当前 workspace 状态归 `MainWindowViewModel` 的页面组合与分发职责。
- 路径校验、规范化、目录存在性检查后续归文件/路径操作能力。
- 等后续确实出现项目配置、显示名、最近打开列表、Git root 映射等业务字段时，再重新引入真正的 workspace 模型。

## 后续约定

- 阶段 2 接入目录选择时，选择结果先经过路径校验和规范化，再调用 `MainWindowViewModel.SetWorkspacePath(...)`。
- 不新增只包装 `CurrentWorkspacePath` 的 service。
- 不在 tab 之间通过全局变量或事件总线传递 workspace path。
- 如果未来重新引入 workspace 模型，应当有超过路径字符串的明确业务字段或行为。
