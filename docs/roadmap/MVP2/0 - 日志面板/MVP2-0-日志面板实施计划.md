# MVP2 阶段 0：日志面板实施计划

## 1. 目标

在主窗口增加一个独立 `Logs` 页签，用只读文本框展示应用当前进程的日志，并提供手动清空入口。

本阶段只做日志查看面板，不做日志搜索、过滤、级别筛选、文件打开、历史日志切换、自动滚动配置、日志导出或外部日志系统接入。

## 2. 当前基础

已有代码边界：

- `Program.cs` 在应用启动时调用 `Log.InitializeFileOutput()`，退出时调用 `Log.Shutdown()`。
- `HaloCreek.Logging.Log` 是当前统一日志入口，负责格式化日志行并写入 `%AppData%/HaloCreek/Logs` 下的当前进程日志文件。
- `Log.Write(...)` 已经集中处理所有日志级别，适合作为 UI 日志流的唯一追加点。
- `MainWindow.axaml` 使用原生 `TabControl`，当前页签包括 `Prompt Editor`、`History Sessions`、`Git`。
- `MainWindowViewModel` 显式持有各 tab 的 ViewModel，并通过 `_tabViewModels` 支持 `ITabSelectionAware`。
- 现有 tab view 位于 `Views/Tabs`，对应 ViewModel 位于 `ViewModels/Tabs`。

阶段 0 优先沿用这些边界。UI 不直接读取日志文件，不直接监听文件变化；日志面板从 Logging 层提供的当前进程内存日志源读取数据。

## 3. 关键决策

- 展示范围：只展示当前应用进程启动后的日志，不展示历史日志文件。
- 早期日志：`Log.InitializeFileOutput()` 到 UI 创建完成之间产生的日志必须能出现在面板中，因此 Logging 层需要保留内存快照。
- 清空语义：`Clear` 只清空日志面板的内存展示内容，不删除、不截断、不改写落盘日志文件。
- 数据来源：在 `HaloCreek.Logging` 内增加有界内存日志缓冲和文本快照变更事件；`Log.Write(...)` 写文件后同步更新该缓冲。
- UI 线程：日志可能来自后台线程，`LogPanelViewModel` 接收文本快照变更事件后必须切回 Avalonia UI 线程更新绑定属性。
- 容量控制：内存日志缓冲必须有上限，避免长时间运行后 `TextBox.Text` 无限增长。MVP 建议先按字符数上限实现，例如保留最近 200KB 文本。
- 失败边界：日志 UI 订阅失败、UI 更新失败不能反向影响 `Log.Write(...)` 的文件写入路径；必要时只捕获并忽略 UI 订阅分发异常。

## 4. 建议实现

### 4.1 扩展 Logging 层

在 `HaloCreek.Logging` 内增加一个明确的当前进程日志源，建议命名为 `CurrentLogText` 或 `LogTextBuffer`。

职责：

- 保存最近日志文本快照。
- 提供 `GetSnapshotText()` 返回当前展示文本。
- 提供 `TextChanged` 事件，参数直接携带当前完整文本快照。
- 提供 `Clear()` 清空内存缓冲，并触发 `TextChanged`，事件快照为空字符串。
- 对缓冲区访问加锁，和 `Log.Write(...)` 的文件 writer 锁保持边界清晰。
- 只接收已经格式化完成的文本，不重新理解日志级别、分类或异常结构。
- 缓冲区裁剪由 Logging 层完成，调用方不需要拼接日志行、处理裁剪或维护自己的展示 buffer。

`Log.Write(...)` 调整：

- 继续先执行当前参数校验。
- 在 `WriterLock` 内保持现有文件写入行为。
- 将 `FormatLine(...)` 的结果保存到局部变量，避免文件输出和 UI 输出格式不一致。
- 文件写入成功后追加到当前进程日志缓冲，并发布新的完整文本快照。
- `_writer is null` 时保持当前行为：忽略该条日志，不追加到 UI 缓冲。

### 4.2 增加 LogPanelViewModel

新增 `HaloCreek/ViewModels/Tabs/LogPanelViewModel.cs`。

建议属性和命令：

- `string LogText`
- `IRelayCommand ClearCommand`

建议行为：

- 构造时读取 Logging 层快照，初始化 `LogText`。
- 订阅文本快照变更事件，收到新快照后直接设置 `LogText`。
- 快照更新和清空都通过 Avalonia UI dispatcher 更新绑定属性。
- `ClearCommand` 调用 Logging 层 `Clear()`；清空后的 `LogText` 由文本快照变更事件统一更新。
- 如实现事件订阅，ViewModel 应实现 `IDisposable` 并在应用退出时解除订阅，避免静态事件持有 ViewModel。

不在 ViewModel 中：

- 读取 `%AppData%` 下的日志文件。
- 自己格式化日志行。
- 解析日志级别或按 category 建索引。

### 4.3 增加 LogPanelView

新增 `HaloCreek/Views/Tabs/LogPanelView.axaml`。

UI 结构建议：

- 外层沿用现有 tab 风格：`Grid Margin="20"`。
- 顶部右侧放一个 `Clear` 按钮。
- `Clear` 按钮始终可用；日志为空时点击是 no-op，不额外做置灰状态。
- 主区域使用 `TextBox`：
  - `IsReadOnly="True"`
  - `AcceptsReturn="True"`
  - `TextWrapping="NoWrap"`
  - `ScrollViewer.VerticalScrollBarVisibility="Auto"`
  - `ScrollViewer.HorizontalScrollBarVisibility="Auto"`
  - `Text="{Binding LogText, Mode=OneWay}"`
- 日志为空时直接保持空文本框；MVP 不单独做空态层，也不为此增加 ViewModel 状态属性。

自动滚动：

- MVP 先不实现自动滚动到底，降低控件行为复杂度。人工验收后再决定是否需要调整。

### 4.4 接入主窗口

`App.axaml.cs`：

- 创建 `LogPanelViewModel`。
- 传入 `MainWindowViewModel`。
- 如果 `LogPanelViewModel` 实现 `IDisposable`，在 `AppServices.Dispose()` 中释放。

`MainWindowViewModel`：

- 构造函数新增 `LogPanelViewModel logs` 参数。
- 增加 `public LogPanelViewModel Logs { get; }`。
- 将 `Logs` 加入 `_tabViewModels`。

`MainWindow.axaml`：

- 在 `TabControl` 中增加独立页签：
  - Header 建议为 `Logs`。
  - 内容为 `<tabs:LogPanelView DataContext="{Binding Logs}"/>`。

## 5. 任务拆分

- [ ] **0-T01 增加当前进程日志缓冲**：在 Logging 层实现有界内存快照、文本快照变更事件和清空能力；验收点是应用启动早期日志能通过快照读取，缓冲不会无限增长，调用方不需要自行维护日志 buffer。
- [ ] **0-T02 接入 Log.Write**：让 `Log.Write(...)` 在保持现有文件输出行为的同时追加到日志缓冲；验收点是现有调用方不需要改动，文件日志格式和面板日志格式一致。
- [x] **0-T03 增加 LogPanelViewModel**：实现 `LogText`、`ClearCommand`、文本快照变更订阅和 UI 线程切换；验收点是运行中新增日志能实时进入绑定文本，点击 `Clear` 后文本清空。
- [ ] **0-T04 增加 LogPanelView**：实现只读文本框和清空按钮；验收点是文本框不可编辑，有水平和垂直滚动条，长日志行不会撑坏布局。
- [ ] **0-T05 接入 MainWindow tab**：把 `Logs` 页签接入 `MainWindow.axaml`、`MainWindowViewModel` 和 `App.axaml.cs`；验收点是主窗口出现独立日志页签，切换不影响其他页签行为。
- [ ] **0-T06 人工验证与边界检查**：验证启动日志、Git 操作日志、错误弹窗路径日志、清空行为和应用退出；验收点是清空不删除落盘日志，日志更新不会导致 UI 崩溃。

## 6. 验收标准

- 主窗口存在独立 `Logs` tab。
- `Logs` tab 中使用只读 `TextBox` 展示当前进程日志。
- 应用启动阶段已经写入的日志能在打开 `Logs` tab 时看到。
- 新产生的日志能追加到面板中，不需要手动刷新。
- 点击 `Clear` 后面板文本清空。
- `Clear` 不删除、不截断当前日志文件。
- 日志文本框不可编辑，复制文本仍可用。
- 日志内容过长时 UI 不明显卡死，内存缓冲有明确上限。
- Logging 层不依赖 Avalonia 控件、ViewModel 或 tab 状态。
- ViewModel 不直接读取日志文件，不直接格式化日志行。

## 7. 暂不处理

- 历史日志文件列表和打开。
- 按日志级别、category、关键字过滤。
- 日志导出。
- 自动滚动到底的可配置开关。
- 日志文件路径展示和打开目录入口。
- 多进程日志聚合。
