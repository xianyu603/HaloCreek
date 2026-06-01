# MVP2 A-1：剪贴板监听能力验证实施方案

## 1. 目标

本阶段验证 HaloCreek 是否能稳定感知剪贴板文本变化，并通过日志观察监听结果。

本验证只证明底层剪贴板能力可用：

- 能读取当前剪贴板文本。
- 能在剪贴板文本变化后收到通知。
- 能过滤重复文本，避免日志刷屏。
- 能在应用退出时释放监听资源。
- 能确认 `PlatformClipboardInfrastructure` 作为平台边界是否合适。

本阶段不实现 review 定位，也不把剪贴板内容和 git diff、文件行号、追问、Agent 修改、批注或 mark reviewed 绑定。

## 2. 模块边界

### 2.1 PlatformClipboardInfrastructure

`PlatformClipboardInfrastructure` 是平台基础设施，只表达剪贴板平台能力。

职责：

- 持有读取剪贴板所需的 Avalonia UI 上下文。
- 读取当前剪贴板文本。
- 启动和停止剪贴板文本变化监听。
- 处理 UI thread 调度、轮询间隔、剪贴板 API 不可用、读取异常等平台细节。
- 对外发布中性的文本变化快照。

不负责：

- 判断文本是不是代码。
- 搜索 workspace 或 git 修改文件。
- 识别文件路径、行号、选区范围。
- 决定 UI 如何提示搜不到或多匹配。
- 触发追问、Agent 修改、批注或 mark reviewed。
- 记录 review 状态。

### 2.2 不新增 ClipboardMonitorService

本阶段不新增单纯的 `ClipboardMonitorService`。

原因：

- 如果它只负责 `Start()` / `Dispose()` / 打日志，就是很薄的转发层。
- 单纯剪贴板事件源没有足够业务语义，不值得提前抽象。
- MVP2 真正需要的是“剪贴板内容如何变成 review 上下文”，而不是泛泛的剪贴板监听服务。
- 本阶段的验证日志可以直接在 composition root 的监听回调中完成，并由 `AppDisposeScope` 持有 watcher。

### 2.3 后续 ReviewClipboardContextService

MVP2 后续新增 `ReviewClipboardContextService`，负责把剪贴板文本解释为审阅上下文。

职责预期：

- 订阅或启动 `PlatformClipboardInfrastructure` 的文本监听。
- 维护当前剪贴板文本快照。
- 处理剪贴板文本去重、截断、日志策略和隐私策略。
- 在 git modified files 中搜索剪贴板文本。
- 形成候选 `file + line range`。
- 返回无匹配、多匹配、唯一匹配等结果。
- 为追问、Agent 修改、批注和 mark reviewed 提供临时定位。

该服务不在本阶段实现。

## 3. 关键决策

- 独立类：新增 `PlatformClipboardInfrastructure`，不继续膨胀现有 `PlatformInfrastructure`。
- 能力类型：剪贴板监听是长期运行能力，不做成静态 `Util`。
- 初版监听方式：优先使用定时轮询读取文本，而不是直接上系统级 clipboard hook。
- 日志验证：初版只打印日志，不新增 UI，不改变 review 面板。
- 文本范围：只监听文本剪贴板；图片、文件列表、富文本格式本阶段忽略。
- 监听及时性：调用方只表达“开始监听”，不传轮询间隔；如果初版内部使用定时轮询，间隔属于平台实现细节。
- 去重位置：本阶段由平台层避免连续发布完全相同文本；后续 `ReviewClipboardContextService` 可以再做业务级去重。
- 失败处理：读取失败要显式记日志或返回失败结果，不静默伪造空文本。
- 生命周期：监听对象必须可释放，应用退出时由 `AppDisposeScope` 统一释放。

## 4. 建议接口

### 4.1 剪贴板快照

```csharp
public sealed record ClipboardTextSnapshot(
    string Text,
    DateTimeOffset CapturedAt);
```

`Text` 保持原始文本，不在平台层 trim。是否 trim、截断、搜索，由上层决定。

### 4.2 PlatformClipboardInfrastructure

```csharp
public sealed class PlatformClipboardInfrastructure
{
    public PlatformClipboardInfrastructure(Window owner);

    public Task<string?> GetTextAsync();

    public IDisposable StartTextWatch(
        Action<ClipboardTextSnapshot> textChanged);
}
```

行为约定：

- `GetTextAsync()` 返回当前剪贴板文本。
- 剪贴板为空或当前内容不是文本时返回 `null`。
- 剪贴板 API 不可用或读取失败时抛出明确异常，调用方决定如何记录。
- `StartTextWatch(...)` 立即开始监听。
- 监听只在文本发生变化时调用 `textChanged`。
- `textChanged` 不能为 `null`。
- 返回的 `IDisposable` 释放后不再触发回调。

初版轮询实现建议：

- 使用 Avalonia UI thread 调用 clipboard API。
- 使用 `PeriodicTimer` 或 Avalonia dispatcher timer 均可。
- 如果使用后台 timer，读取剪贴板时必须切回 UI thread。
- 轮询间隔由 `PlatformClipboardInfrastructure` 内部决定，目标是尽量及时且不明显增加 UI/CPU 负担；初版建议内部默认 `500ms`。
- 记录上一份文本，文本完全相同时不发布事件。
- 单次读取异常不终止监听，但要通过日志暴露。

## 5. 组装方式

`App.axaml.cs` 的 composition root 中新增：

- 创建 `PlatformClipboardInfrastructure(mainWindow)`。
- 调用 `platformClipboardInfrastructure.StartTextWatch(...)`。
- 在回调中写验证日志。
- 将返回的 `IDisposable` watcher 放入 `AppDisposeScope`，退出时释放。

这里的监听接入是 A1 能力验证用的临时实现。正式接入 `ReviewClipboardContextService` 后，需要删除 composition root 中直接启动监听和直接写日志的代码。

实现时需要在临时代码旁写 TODO 注释，例如：

```csharp
// TODO MVP2-A: Temporary clipboard watch for capability validation.
// Remove this direct watcher after ReviewClipboardContextService owns clipboard context.
```

`AppCommonRuntime` 是否暴露剪贴板能力，本阶段建议暂不暴露。

原因：

- 初版只有全局监听验证，没有业务调用方。
- 暴露到 `AppCommonRuntime` 容易让 ViewModel 直接读取剪贴板并跳过服务边界。
- 后续 review 模块需要消费剪贴板事件时，再由 `ReviewClipboardContextService` 提供明确的 review-facing API。

如果后续确实需要让多个 service 读取剪贴板，可以再把 `PlatformClipboardInfrastructure` 或 `ReviewClipboardContextService` 纳入 runtime。

## 6. 日志设计

建议日志 category 使用 `Clipboard`。

启动日志：

```text
Clipboard monitor started.
```

变化日志：

```text
Clipboard text changed. length=128 capturedAt=2026-06-01T10:20:30.0000000+08:00 preview="..."
```

失败日志：

```text
Clipboard read failed.
```

停止日志：

```text
Clipboard monitor stopped.
```

注意：

- 日志 preview 只用于验证，不要输出过长文本。
- 剪贴板可能包含密钥、token、私密内容；日志必须截断。
- 如果后续日志面板默认可见，需要考虑是否允许用户关闭剪贴板日志。

## 7. 任务拆分

- [ ] **A1-T01 新增 PlatformClipboardInfrastructure**：封装 Avalonia clipboard 读取和文本变化监听；验收点是可读取当前文本，并能通过 `IDisposable` 停止监听。
- [ ] **A1-T02 新增 ClipboardTextSnapshot**：定义中性的剪贴板文本快照；验收点是平台层事件不携带 review 语义。
- [ ] **A1-T03 接入 App composition root**：创建平台剪贴板能力，临时启动监听，在回调中打印验证日志，并纳入退出释放；临时代码旁必须写 TODO，说明正式 `ReviewClipboardContextService` 接入后删除；验收点是应用启动后自动监听，退出时无后台任务残留。
- [ ] **A1-T04 验证日志截断策略**：监听回调中只输出文本长度、捕获时间和短 preview；验收点是长文本不会完整写入日志，多行文本不会破坏日志格式。
- [ ] **A1-T05 人工验证**：复制普通文本、多行代码、空文本、重复文本和长文本；验收点是日志符合预期，应用无明显卡顿或异常。
- [ ] **A1-T06 边界复核**：确认平台层没有 git/review/workspace 语义，本阶段没有新增单纯转发型剪贴板 service。

## 8. 验收标准

- 应用启动后日志出现剪贴板监听启动记录。
- 修改剪贴板文本后，日志能看到变化记录。
- 日志包含文本长度、捕获时间和截断 preview。
- 重复复制同一段文本，不产生连续重复变化日志。
- 多行文本不会破坏日志格式。
- 长文本不会完整写入日志。
- 剪贴板为空或不是文本时不会崩溃。
- 单次读取失败不会终止整个监听。
- 关闭应用后监听释放，不留下持续运行的 timer/task。
- `PlatformClipboardInfrastructure` 不依赖 `Services` 层。
- 本阶段没有新增 `ClipboardMonitorService` 这类薄应用服务。

## 9. 暂不处理

- 系统级剪贴板 hook。
- 监听图片、文件列表、HTML、RTF 等非纯文本格式。
- 把剪贴板内容映射到 git diff 文件和行号。
- Review panel UI。
- 用户配置轮询间隔。
- 剪贴板日志开关。
- 多窗口或多应用实例之间的监听协调。
- 权限、隐私提示和敏感内容识别。

## 10. 后续演进

验证通过后再进入 review 定位链路：

1. 增加 review-facing 的剪贴板上下文事件。
2. 新增 `ReviewClipboardContextService`，集中处理剪贴板策略和 review 定位。
3. 在 git modified files 中按剪贴板文本搜索候选位置。
4. 区分无匹配、多匹配、唯一匹配。
5. 将唯一匹配作为临时 `CurrentReviewLocation`。
6. 让追问、Agent 修改、批注和 mark reviewed 消费该定位。

后续如果轮询带来性能或响应问题，再考虑替换 `PlatformClipboardInfrastructure` 内部实现。上层 `ReviewClipboardContextService` 不应感知具体监听机制。
