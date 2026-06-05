# Workspace Runtime Contract 调整计划

本文记录 workspace 管理收束的设计结论和后续修改计划。

## 背景问题

当前 workspace 管理已经有失控倾向：

- workspace 归属不清。之前曾尝试让 top VM 感知 workspace，子 VM 和 service 只把 workspace 当参数，但实际会导致 service 反复校验 workspace 非空、合法、存在。
- workspace 切换逻辑重复。很多业务都在自己写 `OnWorkspaceChanged`，并处理“workspace 切换后中断当前工作、刷新展示”等类似流程。
- 应用能力模型缺失。业务各自兼容空 workspace、非 Git workspace、Git 子目录等情况，处理方式不一致，也容易遗漏。

## 设计结论

新增 `WorkspaceRuntime` static 类作为应用级 workspace contract provider。

workspace 不是普通业务参数，而是应用 runtime state。业务层读取 workspace 时，应拿到已经验证过的应用承诺，而不是自己再判断 workspace 是否为空、是否存在、是否是 Git 仓库。

MVP 阶段只支持一种完整 workspace 能力，不做分级 capability：

- workspace path 必须是已存在且可访问的目录。
- workspace config 必须可加载。
- workspace 必须是 Git repository。
- MVP 阶段要求选择路径就是 Git root，不接受 Git 子目录。

如果用户选择的路径不满足完整能力，拒绝切换 workspace，并用弹窗说明原因。旧 workspace 保持不变。

## 全局访问方式

允许将 workspace runtime 作为应用级全局入口，而不是在所有构造函数里显式传递 workspace 依赖。

但全局入口只暴露已经初始化好的 runtime，不暴露任意可写状态。初始化应只发生在应用组装处，并且只初始化一次。

示意：

```csharp
public static class WorkspaceRuntime
{
    public static WorkspaceContext Current { get; }

    public static event EventHandler<WorkspaceChangedEventArgs>? Changed;

    public static void Initialize(
        AppCommonRuntime appCommonRuntime,
        WorkspaceCacheService workspaceCacheService,
        ConfigService configService);

    public static WorkspaceSwitchResult TrySwitchWorkspace(string requestedPath);
}
```

`AppCommonRuntime` 暂不承载 workspace。它当前语义更接近平台基础设施和通用横切服务；workspace 作为业务 runtime state，单独建立入口更清晰。

现有 `WorkspaceRuntimeService` 不作为最终设计继续扩展。它应被视为迁移期旧类：先新增 `WorkspaceRuntime`，再逐步把业务接线迁移到 static runtime，最后删除 `WorkspaceRuntimeService`。不要在两个类之间长期维持一套“双 runtime”模型。

## Workspace Context

workspace runtime 对业务暴露不可变上下文：

```csharp
public sealed record WorkspaceContext(
    string WorkspacePath,
    AppConfig EffectiveConfig,
    string GitRootPath);
```

`WorkspacePath` 和 `GitRootPath` 在 MVP 阶段应相同。保留 `GitRootPath` 是为了让语义明确：当前应用承诺的是 Git root workspace，而不是普通目录。

暂不引入 `Generation`。如果后续需要处理 `A -> B -> A` 这类异步结果过期问题，应由 workspace runtime 提供统一的 workspace-bound operation 能力，业务不应感知 generation 或自行处理 ABA。

## 切换行为

旧的 `SetWorkspacePath(string)` 不作为最终接口保留。`WorkspaceRuntime` 对外提供表达完整结果的切换接口，例如：

```csharp
public WorkspaceSwitchResult TrySwitchWorkspace(string requestedPath);
```

成功时：

- normalize 选择路径。
- 加载 effective config。
- 校验 Git root。
- 替换当前 `WorkspaceContext`。
- 缓存 workspace path。
- 发布 workspace changed 事件。

失败时：

- 不替换当前 workspace。
- 不缓存失败路径。
- 返回明确失败原因。
- 由 UI 触发方弹窗说明失败原因。

失败原因至少覆盖：

- 路径为空或不可访问。
- 路径不是 Git repository。
- 路径是 Git repository 的子目录，要求选择 Git root。
- workspace config 读取失败。

## 业务侧约束

业务代码不再把空 workspace、非 Git workspace、Git 子目录当成正常分支处理。

允许保留前置条件断言，用于暴露接线错误或违反应用承诺的状态：

```csharp
var workspace = WorkspaceRuntime.Current;
```

或：

```csharp
var workspace = WorkspaceRuntime.RequireCurrent();
```

但不应继续写：

```csharp
if (string.IsNullOrWhiteSpace(workspacePath))
{
    return ...;
}
```

也不应在每个业务入口重复做 capability 检查。MVP 的能力检查只发生在启动和切换 workspace 时。

## 订阅边界

workspace 是应用级 runtime state，允许需要响应 workspace 变化的 VM 或 service 自己订阅 `WorkspaceRuntime.Changed`。

不强制由 top VM 统一转发 workspace。谁订阅，谁负责退订。

需要避免的是同一刷新链路被父 VM 和子 VM 同时触发。例如 Review 和 Git 组件都可以订阅 workspace，但 Review 不应在自己的 workspace handler 里再手动触发 Git 组件刷新，避免重复刷新和边界耦合。

## 异步结果有效性

当前阶段暂不处理 ABA。

如果后续需要统一处理依赖 workspace 的异步结果，应把能力收进 `WorkspaceRuntime`，而不是让每个业务自己比较 path 或 generation。

预期方向：

- workspace runtime 提供 workspace-bound operation ticket。
- ticket 携带当前 context 和 cancellation token。
- workspace 切换时取消旧 ticket。
- 异步结果应用前交给 workspace runtime 判断是否仍属于当前 workspace。

业务只表达“这个异步任务依赖当前 workspace”，不自行决定过期策略。

## 迁移策略

不要在现有 `WorkspaceRuntimeService` 上原地修改成半新半旧形态。这样会在迁移中同时存在 nullable workspace、full workspace contract、旧事件和新事件，边界反而更不清晰。

迁移期采用新旧分离：

1. 新增 `WorkspaceRuntime` static 类，内部独立持有 `WorkspaceContext`、切换逻辑和 changed 事件。
2. 应用启动时先初始化 `WorkspaceRuntime`，并确保初始化成功后才创建依赖 workspace 的业务对象。
3. 新代码只接 `WorkspaceRuntime`，不再新增 `WorkspaceRuntimeService` 引用。
4. 逐步替换旧业务里的 `WorkspaceRuntimeService` 依赖。
5. 替换过程中避免同一个业务同时订阅新旧 workspace 事件。
6. 当旧引用清零后删除 `WorkspaceRuntimeService` 和旧事件参数模型。

迁移过程中只能有一个实际写入当前 workspace 状态的入口。用户触发 workspace 切换时应尽早改为调用 `WorkspaceRuntime.TrySwitchWorkspace(...)`，避免新旧 runtime 各自维护一份当前 workspace。

## 修改计划

1. 新增 `WorkspaceContext` 和 `WorkspaceSwitchResult`。
2. 新增 `WorkspaceRuntime` static 类，内部持有非空 `WorkspaceContext`，并在应用组装处初始化一次。
3. 将启动 workspace 初始化和用户切换统一走 `WorkspaceRuntime` 的完整 workspace 校验。
4. 修改 footer 的 workspace 选择逻辑：切换失败时弹窗说明，旧 workspace 不变。
5. 修改 `GitService`、`ReviewSnapshotService`、`SessionLifecycleService`、History 相关服务，移除空 workspace 和非 Git workspace 的正常分支兼容。
6. 清理 VM 中重复的 workspace 空值判断，只保留刷新展示和切换中断逻辑。
7. 检查 Review 内部 Git 组件刷新链路，避免父子 VM 在同一次 workspace 切换里重复刷新。
8. 删除所有 `WorkspaceRuntimeService` 引用后，删除 `WorkspaceRuntimeService` 和 `WorkspaceRuntimeChangedEventArgs`。
9. 更新 README 或相关 roadmap 文档，说明 MVP workspace 选择必须是 Git root。

## 用户可感知影响

- 选择非 Git 目录时不再切换到该目录，而是弹窗提示必须选择 Git repository root。
- 选择 Git 子目录时不再尝试兼容，而是弹窗提示选择仓库根目录。
- 业务页不再展示“无 workspace”类兼容文案；应用启动和切换后始终应处于有效 workspace。
- 如果应用内部出现空 workspace 或无效 workspace，应视为应用前置条件被破坏，而不是静默降级。
