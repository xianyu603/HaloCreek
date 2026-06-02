# MVP2-A-3 SendPrompt 实施方案

## 1. 目标

在 Review 面板实现 `Send Prompt` 动作，用一个通用入口替换原计划中的“追问”和“小范围修改”。

用户点击 `Send Prompt` 后弹出一个带多行文本框的确认弹窗：

- 如果当前剪贴板定位结果是唯一匹配，则把定位信息填入默认文本。
- 用户可以编辑文本。
- 点击确认后，把文本发送给当前 `FrontSession`。
- 点击取消不发送。
- 没有 `FrontSession` 时，`Send Prompt` 按钮置灰，不允许打开弹窗。

本阶段不区分“只追问”和“让 Agent 修改代码”。是否修改由用户在弹窗文本里表达，不在产品入口上拆分。

## 2. 当前状态

### 2.1 已有能力

- `ReviewView.axaml` 顶部 action bar 已有 `Send Prompt` 占位按钮。
- `ReviewViewModel` 已持有：
  - `ReviewClipboardContextService`，并维护最近一次 `_clipLocateResult`。
  - `SessionLifecycleService`，并维护 `HasFrontSession`。
  - `AppCommonRuntime`，可访问 `PlatformInfrastructure`。
- `SessionLifecycleService.SendMessageToFrontSession(string message)` 已负责：
  - 空消息拒绝。
  - 没有 front session 时报告失败。
  - 发送到 front session。
- `PlatformInfrastructure.ShowMessageDialogAsync(...)` 已有简单弹窗模式，但只能展示消息，不能输入文本。

### 2.2 缺口

- Review 面板缺少 `SendPromptCommand`。
- 缺少可复用的文本输入弹窗 API。
- 缺少把 `ReviewClipboardClipLocateResult` 格式化为默认 prompt 文本的规则。
- `Send Prompt` 按钮尚未绑定 `HasFrontSession` 或命令可执行状态。

## 3. 影响面

### 3.1 UI 层

文件：`HaloCreek/Views/Tabs/ReviewView.axaml`

- 给 `Send Prompt` 按钮绑定 `Command="{Binding SendPromptCommand}"`。
- 按钮置灰优先依赖 command `CanExecute`，必要时同时绑定 `IsEnabled="{Binding HasFrontSession}"` 保持行为直观。
- 不新增“追问”和“小范围修改”按钮。

可感知变化：

- 没有 front session 时按钮不可点击。
- 有 front session 时点击按钮会弹出输入框，而不是立即发送。

### 3.2 ViewModel 层

文件：`HaloCreek/ViewModels/Tabs/ReviewViewModel.cs`

新增：

- `IAsyncRelayCommand SendPromptCommand`。
- `SendPromptAsync()`：
  1. 读取当前 `_clipLocateResult`。
  2. 生成默认文本。
  3. 调用平台输入弹窗。
  4. 用户确认且文本非空时调用 `_sessionLifecycleService.SendMessageToFrontSession(...)`。
- `CanSendPrompt()`：返回 `HasFrontSession`。

调整：

- `HasFrontSession` 变化时同时调用 `SendPromptCommand.NotifyCanExecuteChanged()`。
- 默认文本由定位结果模型提供格式化方法，`ReviewViewModel` 只负责调用和拼装弹窗流程。

边界：

- 不在 ViewModel 内部重新判断 front session id，继续以 `SessionLifecycleService` 作为发送边界。
- 不缓存弹窗文本。每次打开都基于当前定位重新生成默认文本。
- 不新增单独 formatter service；格式化逻辑直接落在定位结果模型附近，供 SendPrompt 和双击定位结果按钮共用。

### 3.3 平台弹窗层

文件：`HaloCreek/Infrastructure/PlatformInfrastructure.cs`

新增一个明确语义的输入弹窗 API，例如：

```csharp
public Task<string?> ShowTextInputDialogAsync(
    string title,
    string initialText,
    string confirmText,
    string cancelText)
```

返回约定：

- 用户确认：返回文本框内容。
- 用户取消或关闭窗口：返回 `null`。

实现方式：

- 延续当前 `ShowMessageDialogAsync` 的轻量 Window 构建方式。
- 内容为一个多行 `TextBox` 和确认/取消按钮。
- `AcceptsReturn = true`，`TextWrapping = Wrap`，设置合理高度。
- 确认按钮可以始终可点，空白文本交给 `SessionLifecycleService.SendMessageToFrontSession` 继续拒绝；更推荐在弹窗内禁用确认按钮以减少无效操作，但这会增加少量输入状态绑定代码。

本阶段建议采用：确认按钮始终可点，发送前在 `ReviewViewModel` 对空白文本直接返回，不打扰用户。

原因：

- `SessionLifecycleService` 已有空文本保护。
- Review 的 SendPrompt 是窄入口，不需要为一个弹窗引入新 ViewModel 或复杂绑定。
- 符合 `DesigningRules.md` 中“短且线性的单点调用不要拆成很多私有方法”的约束。

### 3.4 剪贴板定位结果

数据来源：`ReviewClipboardContextService.CurrentClipLocateResult` / `_clipLocateResult`

默认文本只在以下条件成立时生成定位信息：

- `Status == ReviewClipboardClipLocateStatus.UniqueMatch`
- `ClipLocate is not null`

默认文本：

```text
<relative path>, line <start line> col <start col> to line <end line> col <end col>

```

单点或单行不特殊降级为 `Lines: ...`，仍使用同一种 range 语义。这样可以准确表达列范围，也避免把跨行范围误读为“整段行范围”。
不默认注入“请不要修改代码”或“请直接修改代码”等固定话术，因为本功能已经合并“追问”和“小范围修改”，具体意图应由用户输入。

本阶段不默认注入剪贴板原文，避免把大段代码或敏感内容无提示发送给 front session。用户需要引用原文时，可以在弹窗里自行补充。

### 3.5 定位结果格式化复用

文件：`HaloCreek/Models/ReviewClipboardClipLocateResult.cs`

当前 `ReviewViewModel.ShowClipLocateResultAsync()` 已经手写了一套定位结果展示文本。`SendPrompt` 如果再手写默认文本，会导致行列范围表达分叉。

建议分成两层格式化接口：

1. `ReviewClipLocateCandidate` 描述当前位置。
2. `ReviewClipboardClipLocateResult` 描述本次定位结果，包含成功和失败信息；成功时调用 `ReviewClipLocateCandidate` 的格式化接口。

`ReviewClipLocateCandidate` 已包含相对路径和行列范围：

```csharp
public sealed record ReviewClipLocateCandidate(
    string RelativePath,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
```

因此 candidate 层可以提供当前位置文本，例如：

```csharp
public string FormatLocationText()
```

返回：

```text
<relative path>, line <start line> col <start col> to line <end line> col <end col>
```

`ReviewClipboardClipLocateResult` 层提供定位结果文本，例如：

```csharp
public string FormatResultText()
```

成功时输出：

```text
Matched
<relative path>, line <start line> col <start col> to line <end line> col <end col>
```

失败时输出：

```text
Unmatched
Failure reason: <failure reason>
<message>
```

共用点：

- `SendPrompt` 默认文本只在成功时使用 `ClipLocate.FormatLocationText()`，避免把失败原因默认注入 prompt。
- 双击定位结果按钮直接使用 `ReviewClipboardClipLocateResult.FormatResultText()`，统一成功和失败展示。

边界：

- `ReviewClipLocateCandidate` 只表达当前位置，不知道成功/失败状态。
- `ReviewClipboardClipLocateResult` 只表达定位结果展示文本，不知道弹窗标题、按钮文案、front session 或发送行为。
- `ReviewViewModel` 只负责选择哪个文本进入哪个 UI 场景：SendPrompt 用成功定位文本作为默认 prompt；双击定位结果按钮用完整结果文本。

### 3.6 FrontSession 行为

使用现有 `SessionLifecycleService.SendMessageToFrontSession`。

不新增发送服务，也不绕过 `SessionLifecycleService` 直接调用 `TmuxService`。

竞态处理：

- 按钮打开时基于 `HasFrontSession` 置灰。
- 用户打开弹窗后如果 front session 消失，确认时由 `SessionLifecycleService.SendMessageToFrontSession` 报告失败。
- 本阶段不处理用户切换 front session 的更细粒度一致性，沿用 MVP2 feature list 中“发送到当前 front session”的约束。

## 4. 实施步骤

1. 在 `PlatformInfrastructure` 增加 `ShowTextInputDialogAsync(...)`。
2. 在 `ReviewClipLocateCandidate` 增加当前位置格式化方法。
3. 在 `ReviewClipboardClipLocateResult` 增加成功/失败结果格式化方法，成功分支调用 candidate 格式化方法。
4. 调整 `ReviewViewModel.ShowClipLocateResultAsync()`，让双击定位结果按钮复用 result 格式化方法。
5. 在 `ReviewViewModel` 增加 `SendPromptCommand` 和 `SendPromptAsync()`。
6. 在 `HasFrontSession` setter 中通知 `SendPromptCommand` 可执行状态变化。
7. 在 `ReviewView.axaml` 给 `Send Prompt` 按钮绑定 command。
8. 构建验证。

## 5. 验收标准

- 没有 front session 时，`Send Prompt` 按钮置灰且不能打开弹窗。
- 有 front session 时，点击 `Send Prompt` 弹出多行文本输入弹窗。
- 当前有唯一剪贴板定位结果时，弹窗默认文本包含相对路径和 `line <start> col <start> to line <end> col <end>` 范围。
- 双击定位结果按钮使用 `ReviewClipboardClipLocateResult` 的结果格式化文本，成功分支和 `SendPrompt` 默认文本共用同一个 candidate 位置格式。
- 当前没有唯一定位结果时，弹窗默认文本为空。
- 点击取消或直接关闭弹窗，不发送消息。
- 点击确认且文本非空时，调用 `SessionLifecycleService.SendMessageToFrontSession(...)`。
- 点击确认但文本为空或全空白时，不发送。
- 弹窗打开后 front session 消失时，确认发送失败由现有失败提示处理。

## 6. 验证建议

### 6.1 编译

使用仓库当前 Avalonia 构建路径验证：

```powershell
dotnet build HaloCreek/HaloCreek.csproj
```

如果在 WSL 环境中走 Windows dotnet，则继续沿用本仓库现有 build-review 流程。

### 6.2 手工验证

1. 启动应用，不创建 front session，确认 `Send Prompt` 为 disabled。
2. 创建或切到一个 front session，确认按钮变为 enabled。
3. 复制一段能唯一匹配 changed file 的文本，等待定位状态变为 `Matched`。
4. 点击 `Send Prompt`，确认默认文本包含定位信息。
5. 修改文本后点击确认，确认消息进入 front session。
6. 再次打开弹窗后点击取消，确认 front session 没有新增消息。
7. 复制无法定位的文本，确认弹窗默认文本为空。

## 7. 暂不实现

- 不拆分“追问”和“Agent 小范围修改”两个按钮。
- 不新增 prompt 模板系统。
- 不为定位信息增加固定限制话术。
- 不默认发送剪贴板原文。
- 不处理 front session 在弹窗打开期间被切换后的目标锁定。
- 不新增发送历史或 Review prompt 草稿持久化。

## 8. 风险和后续

- 默认文本只有定位信息，用户可能仍需补充问题或修改意图；这是合并入口后的预期行为。
- `PlatformInfrastructure` 会继续承载一个轻量输入弹窗；如果后续弹窗数量增加，再考虑抽通用 dialog 组件。
- 如果未来需要固定话术，应放到 Prompt 编辑或模板能力里，不应重新拆出“追问/修改”两个业务入口。
