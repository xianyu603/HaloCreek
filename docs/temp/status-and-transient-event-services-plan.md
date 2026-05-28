# Status And Transient Event Services Plan

本文记录 `error-handling-and-status-bar-principles.md` 对应的短期实施方案。

## 目标

- 取消各 Tab ViewModel 直接写 Footer `StatusText`。
- Status bar 只展示当前仍有效的应用全局状态。
- 一过性事件不参与 status bar 仲裁，由独立策略入口处理。
- 状态来源和展示 UI 解耦，方便后续调整错误处理政策。

## 新增服务

### ApplicationStatusService

负责维护应用当前状态，并对外发布已经仲裁后的 status bar 展示文本。

建议接口形态：

```csharp
public sealed class ApplicationStatusService
{
    public event Action<string>? StatusTextChanged;

    public ApplicationStatusHandle BeginBackgroundTask(string message);
    public ApplicationStatusHandle SetGlobalError(string message);
    public void Clear(ApplicationStatusHandle handle);
}
```

调用方不自己生成或保存业务 key。`BeginBackgroundTask(...)` / `SetGlobalError(...)` 返回服务生成的 `ApplicationStatusHandle`，后续只把该 handle 回传给 `Clear(...)`。这样可以避免不同调用点约定 key 字符串，也避免 key 生命周期散落在 ViewModel 中。

暂不新增 `ApplicationStatusChangedEventArgs`。当前只发布 status bar 需要展示的文本；后续如果确实要扩展字段，再同步调整服务事件和订阅方接口。

服务内部维护状态项、优先级和清除逻辑。当前优先级为：

1. 持续性全局错误。
2. 正在进行的后台任务。
3. 空。

如果同一类状态存在多项，先采用简单策略：显示最近设置的一项。后续需要更复杂仲裁时只改服务内部。

### TransientEventService

负责处理一过性事件，不向 status bar 发布状态。

建议接口形态：

```csharp
public sealed class TransientEventService
{
    public void ReportInfo(string category, string message);
    public Task ReportUserActionFailureAsync(string title, string message);
    public void ReportBackgroundFailure(string category, Exception exception, string message);
}
```

当前策略保持克制：

- 普通成功、取消、后台刷新成功：只写日志或不处理。
- 可恢复后台失败：只写日志。
- 用户刚触发且必须知道结果的失败：调用错误弹窗。

后续如果增加 toast、错误中心或 tab-local 提示，只调整该服务策略，不让业务 ViewModel 直接依赖具体 UI。

## 接入步骤

1. 在 `App.axaml.cs` 创建两个服务实例，并注入需要上报状态或事件的 ViewModel。
2. `WorkspaceFooterViewModel` 改为监听 `ApplicationStatusService.StatusTextChanged`，只根据服务输出更新 `StatusText`。
3. 移除 `PromptEditorViewModel`、`HistorySessionsViewModel`、`GitViewModel` 的 `SetStatusDispatcher(...)`。
4. 将现有 `_statusDispatcher?.Invoke(...)` 分类迁移：
   - 持续性全局错误或后台任务进入 `ApplicationStatusService`。
   - 普通操作结果、取消、可恢复后台失败进入 `TransientEventService` 或只写日志。
5. `MainWindowViewModel` 只保留 workspace 分发和 tab 协调，不再承担 status message 转发。

## 验收点

- 任意 Tab 不再直接引用 Footer 或设置 Footer status 文本。
- 一过性消息不会覆盖持续性全局错误。
- 后台任务结束必须通过返回的 `ApplicationStatusHandle` 清除自身状态。
- 后台刷新失败保留现有可用数据时，不再写入 status bar。
- 用户必须知道的操作失败仍有弹窗或明确可见反馈。
