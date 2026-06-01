# MVP2 A-2：定位能力验证实施方案

## 1. 目标

本阶段验证 HaloCreek 是否能把剪贴板文本定位到当前 workspace 的 Git 变更文件，并通过日志观察定位结果。

本验证只证明“剪贴板文本 -> 文件和行号”的底层链路可用：

- 能接收 `PlatformClipboardInfrastructure` 发布的剪贴板文本变化。
- 能读取当前 workspace。
- 能获取当前 Git working tree 中新增或修改的文件。
- 能在这些文件的当前磁盘内容中搜索剪贴板文本。
- 能计算匹配位置的文件、起止行号和列号。
- 能区分唯一匹配和失败，并在失败原因中说明无匹配、多匹配、无 workspace 等情况。
- 能把定位 policy 收敛在新增的 review-facing service 中。
- 能通过日志验证定位结果。

本阶段不接 UI，不向 front session 发消息，不触发追问、Agent 修改、批注或 mark reviewed。

## 2. 模块边界

### 2.1 PlatformClipboardInfrastructure

`PlatformClipboardInfrastructure` 继续只表达平台剪贴板能力。

职责：

- 读取剪贴板文本。
- 发布中性的 `ClipboardTextSnapshot`。
- 处理 Avalonia clipboard API、UI thread、轮询和运行期读取异常。

不负责：

- 读取 workspace。
- 查询 Git 状态。
- 扫描文件内容。
- 判断定位唯一性。
- 计算行号。
- 写 review 定位日志。

### 2.2 ReviewClipboardContextService

本阶段新增 `ReviewClipboardContextService`，作为 review-facing 的剪贴板上下文服务。

职责：

- 订阅 `PlatformClipboardInfrastructure.TextChanged`。
- 读取当前 workspace。
- 按本阶段定位 policy 获取 Git working tree 候选文件。
- 在候选文件中搜索剪贴板文本。
- 生成定位结果：唯一匹配或失败；失败结果携带明确原因。
- 记录能力验证日志。
- 保存最近一次定位结果，供后续 UI、追问、Agent 修改、批注和 mark reviewed 消费。
- 在 `Dispose()` 时取消订阅剪贴板事件。

不负责：

- 自己读取系统剪贴板。
- 直接操作 Avalonia UI。
- 直接向 front session 输入 prompt。
- 决定 UI 如何提示无匹配或多匹配。
- 持久化批注或 reviewed 状态。
- 执行 mark reviewed、discard 或 diff。

### 2.3 GitService

`GitService` 继续负责 Git 命令和 Git status 解析。

本阶段建议优先复用现有 `GetChanges(workspacePath)`。如果实现中发现 `GitChangeInfo` 无法可靠表达“当前磁盘文件是否应参与定位”，可以给 `GitService` 增加中性的查询方法，例如 `GetWorkingTreeSearchCandidates(...)`，但筛选 policy 仍由 `ReviewClipboardContextService` 决定。

`GitService` 不负责解释剪贴板文本，也不负责输出 review 定位结果。

## 3. 定位 Policy

定位 policy 暂定只扫描当前 Git working tree 的新增或修改文件。

候选文件来源：

- 从当前 workspace 调用 Git status。
- 只考虑当前磁盘上存在的普通文件。
- 包含 `Added`、`Modified`、`Renamed`、`Copied`、`Untracked`。
- 对 `Renamed` / `Copied` 扫描当前 `RelativePath`。
- 排除 `Deleted`、`Conflicted`、`Unknown`。
- 排除目录、缺失文件、无法读取文件和明显过大的文件，并写日志说明跳过数量。

匹配内容：

- 如果剪贴板文本为空或全空白，直接返回 `Failed`，失败原因为 `IgnoredEmptyClipboardText`。
- 搜索前统一把剪贴板和文件内容的换行规整为 `\n`，避免 Windows / Unix 换行差异影响定位。
- 搜索前固定去掉剪贴板文本末尾的换行符，避免复制整行或多行时编辑器附带的末尾换行影响定位。
- 不做大小写折叠。
- 不做空白压缩。
- 不做 fuzzy match。
- 不跨文件匹配。

匹配结果：

- 找到 0 处：`Failed`，失败原因为 `NoMatch`。
- 找到 1 处：`UniqueMatch`，作为临时当前审阅位置。
- 找到第 2 处匹配：立即停止扫描并返回 `Failed`，失败原因为 `MultipleMatches`。

行号计算：

- 行号从 1 开始。
- 列号从 1 开始。
- 多行匹配记录 `StartLine`、`StartColumn`、`EndLine`、`EndColumn`。
- 后续追问、Agent 修改和 mark reviewed 初版可以只消费行号；列号用于日志验证和未来细化范围。

性能限制：

- 单次剪贴板变化触发一次完整扫描。
- 如果上一次定位仍在执行，新的剪贴板变化应取消上一轮扫描，并只保留最新剪贴板文本的定位结果。
- 被取消的扫描不更新 `CurrentLocationResult`，只允许写一条取消日志。
- 文件扫描和文本搜索需要检查取消信号，避免大文件或大量文件导致旧扫描长时间占用。
- 扫描过程中发现第 2 个匹配时立即返回 `Failed`，不继续扫描剩余文件。
- 单文件大小建议先限制为 1 MB 或 2 MB；超过限制跳过并记录数量。

## 4. 建议数据结构

### 4.1 定位结果

```csharp
public sealed record ReviewClipboardLocationResult(
    ReviewClipboardLocationStatus Status,
    ClipboardTextSnapshot ClipboardSnapshot,
    ReviewLocationCandidate? Location,
    string? FailureReason,
    string Message);

public enum ReviewClipboardLocationStatus
{
    UniqueMatch,
    Failed
}
```

约定：

- `UniqueMatch` 时 `Location` 必须非空，`FailureReason` 为 `null`。
- `Failed` 时 `Location` 为 `null`，`FailureReason` 必须非空。
- 暂时不把失败原因提升成 enum；当前没有按失败类型做不同业务分支的需求。

### 4.2 候选位置

```csharp
public sealed record ReviewLocationCandidate(
    string RelativePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
```

### 4.3 ReviewClipboardContextService

```csharp
public sealed class ReviewClipboardContextService : IDisposable
{
    public ReviewClipboardContextService(
        PlatformClipboardInfrastructure platformClipboardInfrastructure,
        WorkspaceRuntimeService workspaceRuntimeService,
        GitService gitService);

    public ReviewClipboardLocationResult? CurrentLocationResult { get; }

    public event EventHandler<ReviewClipboardLocationChangedEventArgs>? LocationChanged;

    public void Dispose();
}
```

行为约定：

- 构造函数订阅 `platformClipboardInfrastructure.TextChanged`。
- 每次剪贴板文本变化后触发定位扫描。
- 扫描完成后更新 `CurrentLocationResult`。
- 扫描完成后发布 `LocationChanged`，供后续 UI 或动作服务接入。
- 本阶段可以没有任何订阅方，验证只看日志。
- `Dispose()` 取消订阅，释放后不再扫描。

本阶段允许 `ReviewClipboardContextService` 自己写能力验证日志。后续接 UI 后，日志可以保留为 debug/info 级辅助信息，UI 展示由 ViewModel 消费 `LocationChanged`。

## 5. 组装方式

`App.axaml.cs` 的 composition root 中新增：

- 创建 `ReviewClipboardContextService(platformClipboardInfrastructure, workspaceRuntimeService, gitService)`。
- 将 `ReviewClipboardContextService` 放入 `AppDisposeScope`。
- 删除 A-1 中 composition root 直接订阅 `platformClipboardInfrastructure.TextChanged` 并打印剪贴板变化的临时代码，或把该日志收敛到 `ReviewClipboardContextService` 中。

建议在组装处保留临时 TODO：

```csharp
// TODO MVP2-A: Temporary review location validation.
// Replace log-only validation after Review panel consumes ReviewClipboardContextService.LocationChanged.
```

`AppCommonRuntime` 本阶段仍不需要暴露剪贴板能力或定位服务。

原因：

- 当前只有 composition root 负责组装，ViewModel 还不消费定位。
- 过早暴露 runtime 属性会鼓励 UI 层直接绕过服务边界。
- 后续 Review panel 真正接入时，再决定由 `MainWindowViewModel` 持有 review ViewModel，还是由 runtime 暴露 review-facing 服务。

## 6. 日志设计

建议日志 category 使用 `ReviewLocation`。

服务启动：

```text
Review clipboard context service created.
```

开始扫描：

```text
Review location scan started. clipboardLength=128 workspace="D:\work\HaloCreek"
```

唯一匹配：

```text
Review location unique match. file="HaloCreek/Services/GitService.cs" startLine=42 startColumn=17 endLine=44 endColumn=5
```

无匹配：

```text
Review location no match. searchedFiles=8 skippedFiles=1 clipboardLength=128
```

多匹配：

```text
Review location multiple matches. searchedFilesBeforeStop=3
Review location first match. file="HaloCreek/App.axaml.cs" startLine=56 startColumn=13 endLine=56 endColumn=40
Review location second match. file="HaloCreek/Services/GitService.cs" startLine=42 startColumn=17 endLine=42 endColumn=44
```

跳过：

```text
Review location scan ignored. reason=IgnoredEmptyClipboardText
Review location scan canceled because a newer clipboard text arrived.
```

失败：

```text
Review location scan failed.
```

注意：

- 不输出完整剪贴板文本。
- preview 如需保留，沿用 A-1 的截断和转义策略。
- 日志必须包含足够信息来人工验证文件和行号是否准确。
- 多匹配场景只需要输出前两个匹配位置，用来证明失败原因和重复位置；不继续枚举全部候选，避免日志刷屏和无意义扫描。

## 7. 验收标准

- 应用启动后日志出现 `Review clipboard context service created.`。
- 复制当前 Git 新增或修改文件中的一段单行文本，日志能输出唯一匹配的文件和行号。
- 复制当前 Git 新增或修改文件中的一段多行文本，日志能输出正确的起止行号。
- 复制未修改文件中的文本，不会匹配。
- 复制空白文本，不扫描文件，并输出忽略原因。
- 复制在多个变更文件中重复出现的文本，日志发现第 2 个匹配后立即输出 `MultipleMatches`。
- 复制找不到的文本，日志输出 `NoMatch`。
- 删除、冲突、未知状态文件不参与扫描。
- 当前 workspace 为空或不是 Git repository 时不崩溃，并输出明确原因。
- 文件读取失败不会终止剪贴板监听。
- 连续快速复制时，新的剪贴板变化会取消上一轮扫描；最终只保留最新剪贴板文本的定位结果。
- 关闭应用后 `ReviewClipboardContextService` 取消订阅，不再响应剪贴板变化。
- 本阶段没有新增 Review UI，没有向 front session 发送消息。

## 8. 暂不处理

- UI 提示无匹配、多匹配、唯一匹配。
- 自动把唯一定位注入 prompt。
- 从 IDE 当前选区、鼠标位置或系统无障碍 API 获取上下文。
- diff hunk 级定位。
- 只定位 unstaged working tree diff。
- 对 staged 内容、HEAD 内容和 working tree 内容做三方区分。
- fuzzy match、忽略空白差异、语法级匹配。
- 二进制文件、超大文件和非文本编码的精确处理。
- 批注持久化定位。
- mark reviewed 的 reviewed baseline。
- 多 workspace、多窗口、多 ongoing session 并发。

## 9. 后续演进

验证通过后再进入 A-3：

1. 让 Review panel 或临时入口消费 `ReviewClipboardContextService.LocationChanged`。
2. 在 UI 中展示当前审阅位置、无匹配和多匹配状态。
3. 唯一匹配时，把 `file + line range` 注入追问 prompt。
4. Agent 修改动作复用同一定位，并增加“只允许修改该范围附近代码”的软约束。
5. 批注使用更稳定的内容锚点，不直接复用本阶段的临时定位结果。
6. mark reviewed 后续独立设计 reviewed baseline，不和本阶段扫描 policy 混在一起。

本阶段的核心边界是：`ReviewClipboardContextService` 拥有剪贴板文本到 review 定位的 policy；平台层只提供剪贴板能力，Git 层只提供中性的 Git 变更信息。
