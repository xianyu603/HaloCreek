# MVP1 阶段 4：History Session Tab 执行说明

## 1. 目标

阶段 4 实现当前 workspace 的历史 Codex session 最小可用闭环：

- 从本机 Codex session 历史文件读取当前 workspace 的历史 session。
- History Sessions 页签展示 session 列表。
- 支持按关键词搜索历史 session。
- 支持对选中 session 执行 `Resume`。
- 支持把历史 session 的初始 prompt 重新送回 Prompt Editor 编辑。

本阶段只接入当前版本 Codex CLI 的本地 session 文件格式，不承诺兼容 Codex 未来文件格式变更。格式不稳定性继续隔离在 `Services/SessionHistory/CodexSessionHistoryReader` 后面，ViewModel 和 UI 只消费稳定模型。

本阶段不实现 full transcript 详情页、不做历史内容全文检索、不实现 ongoing session 状态联动、不引入配置编辑器。

## 2. 当前基础

已有代码边界：

- `HistorySessionsView.axaml`：已有搜索框和列表占位。
- `HistorySessionsViewModel`：已有 `SearchText`、`WorkspacePath`、`Sessions` 和 workspace 切换后刷新逻辑。
- `SessionHistoryService`：已有读取、按更新时间倒序、搜索的服务入口。
- `ISessionHistoryReader`：已有历史 session 原始数据读取边界。
- `MockHistorySessionReader`：当前为 mock 数据来源。
- `CodexSessionHistoryReader`：已有空实现，本阶段切到真实解析。
- `HistorySessionInfo`：已有展示用基础字段，本阶段按新口径重组。
- `PlatformInfrastructure`：已有 Terminal/WSL 拉起能力，可复用来执行 `codex resume`。

## 3. 已确认关键决策

### 3.1 历史数据来源

阶段 4 直接读取本机 Codex CLI 的 JSONL 历史文件：

```text
$CODEX_HOME/sessions/YYYY/MM/DD/*.jsonl
~/.codex/sessions/YYYY/MM/DD/*.jsonl
```

已确认口径：

- `SessionHistoryRootPath` 不作为配置项，需从 `AppConfig` 删除。
- session history root 写死在 `CodexSessionHistoryReader` 内部：优先 `$CODEX_HOME/sessions`，再 fallback 到 `~/.codex/sessions`。
- 只读取 `.jsonl` 文件。
- 单个文件解析失败不影响整个列表，记录为该文件不可用。
- 解析文件数量通过配置限制，默认只解析最新的至多 100 个文件。

### 3.2 workspace 匹配规则

Codex session 首行 `session_meta.payload.cwd` 作为 workspace 归属依据。

已确认口径：

- 只展示 `session_meta.payload.cwd` 等价于当前 workspace 的 session。
- 需要处理 Windows 路径和 WSL 路径等价，例如 `D:\work\HaloCreek` 与 `/mnt/d/work/HaloCreek`。
- 路径等价能力放在 `PlatformInfrastructure` 或底层路径 helper 中，不写在 ViewModel。
- 不按 git 仓库根目录、父子目录或文件名模糊匹配 workspace。

### 3.3 SessionHistory 模型重组

`HistorySessionInfo` 按 resume 和展示需要重新组织。

已确认口径：

- `Id` 使用 resume key；缺失时抛异常，该 session 文件视为解析失败。
- `WorkspacePath` 先使用 `session_meta.payload.cwd`。
- `CreatedAt` 使用 `session_meta.payload.timestamp`；缺失时抛异常。
- `LastUpdatedAt` 使用最后一条有效记录的 `timestamp`；缺失时抛异常。
- 删除 `Title` 字段。
- 删除 `State` 字段和 `HistorySessionState` enum。
- `InitialPromptPreview` 改为 `InitialPrompt`，记录完整初始提示词。
- 补充 `LastReply` 字段，记录最后一条 assistant 回复。
- 补充 `LastPrompt` 字段，记录最后一条用户 prompt。
- 保留 `SessionFilePath` 字段，便于排查和后续详情页使用。

建议模型形态：

```csharp
public sealed record HistorySessionInfo(
    string Id,
    string WorkspacePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    string InitialPrompt,
    string LastPrompt,
    string LastReply,
    string SessionFilePath);
```

### 3.4 prompt 与 reply 提取

先按当前口径提取，完成后看真实展示效果再决定是否调整。

已确认口径：

- `InitialPrompt` 优先读取第一条 `event_msg` 且 `payload.type == "user_message"` 的 `payload.message`。
- 若没有 `event_msg user_message`，再 fallback 到第一条 `response_item` 且 `payload.type == "message"`、`payload.role == "user"` 的文本内容。
- `LastPrompt` 读取最后一条可识别的用户 prompt。
- `LastReply` 读取最后一条可识别的 assistant 回复。
- 跳过仅包含环境上下文的 user message。
- 不解析 tool call 细节，不拼接完整多轮 transcript。
- prompt/reply 原文只保存在内存模型中，不写入 HaloCreek 自己的缓存文件。

### 3.5 搜索范围

已确认口径：

- 搜索只做内存过滤。
- 暂时只搜索 `InitialPrompt`。
- 不搜索完整 JSONL transcript。
- 不搜索 `Id`、`WorkspacePath`、`LastPrompt`、`LastReply`。
- 搜索框变更时即时过滤已加载结果。

### 3.6 Resume 行为

Codex CLI 当前支持：

```text
codex resume [SESSION_ID] [PROMPT]
```

已确认口径：

- History 列表中的 `Resume` 按钮拉起 Terminal，并执行 `codex resume <session-id>`。
- 启动目录使用当前 workspace。列表本身已经按当前 workspace 筛选，不再用 session 记录里的 workspace 改写启动目录。
- Resume 入口放进 `SessionLifecycleService`，例如新增 `Resume(session, currentWorkspacePath, config)`，ViewModel 不直接调用 `Process.Start`。
- 启动结果继续同步到 Footer 状态。
- 阶段 4 不把 resume 后的进程加入 Ongoing Sessions 列表，等阶段 5 统一处理。

### 3.7 Reedit Initial Prompt 行为

已确认采用方案 A：

- 点击 `Reedit` 后，把该 session 的 `InitialPrompt` 填入 Prompt Editor。
- 同时切换到 Prompt Editor 页签。
- 用户修改后再点击 `Launch` 启动新 session。
- `Reedit` 不自动 launch，也不执行 `codex fork`。

### 3.8 加载与性能

已确认口径：

- workspace 切换时重新扫描 session 文件。
- T03 到 T05 只做 workspace 切换触发的一次性加载，不做定时刷新。
- T06 再接通定时刷新，用于 workspace 不变时自动发现新增或变化的 session 文件。
- 不提供手动 `Refresh` 按钮。
- 搜索结果只过滤已加载结果，不反复读文件。
- 阶段 4 不做持久化索引、不做缓存失效策略。
- 解析文件数量通过配置限制，默认解析最新的至多 100 个文件。
- 解析文件数量较多时，优先保证 UI 不崩溃；必要时在 ViewModel 中用 async command 包一层后台加载。

### 3.9 UI 布局

布局决策单拆工作条目处理。阶段 4 先不把布局细节混入数据解析和交互决策。

## 4. 建议实现

### 4.1 调整 AppConfig

从 `AppConfig` 删除 `SessionHistoryRootPath`。Codex session history root 不是 HaloCreek 配置项。

如果需要限制解析文件数量，建议新增明确字段：

```csharp
int MaxSessionHistoryFiles
```

`AppConfig.DefaultForMvp1` 默认设置为 `100`。

### 4.2 重组 HistorySessionInfo

将 `HistorySessionInfo` 调整为：

```csharp
public sealed record HistorySessionInfo(
    string Id,
    string WorkspacePath,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    string InitialPrompt,
    string LastPrompt,
    string LastReply,
    string SessionFilePath);
```

同步删除：

- `Title`
- `InitialPromptPreview`
- `State`
- `HistorySessionState`

### 4.3 实现真实 CodexSessionHistoryReader

`CodexSessionHistoryReader` 负责：

- 定位 Codex session history root。
- 枚举 `.jsonl` 文件，并按文件时间取最新的至多 `MaxSessionHistoryFiles` 个文件。
- 读取 `session_meta`。
- 判断 workspace 是否匹配。
- 提取 resume key、created time、last updated time。
- 提取 `InitialPrompt`、`LastPrompt`、`LastReply`。
- 映射为 `HistorySessionInfo`。
- 对缺失必需字段的文件抛出解析异常，并在文件级跳过。

`SessionHistoryService` 继续只负责排序和搜索，不知道 JSONL 结构。

### 4.4 接通 HistorySessionsViewModel

建议新增能力：

```text
Workspace change load
Timer refresh
ResumeCommand
ReeditInitialPromptCommand
```

ViewModel 调用关系保持为：

```text
HistorySessionsViewModel
├─ SessionHistoryService.SearchSessions(...)
├─ SessionLifecycleService.Resume(...)
└─ Action<string> setPromptText / requestPromptEditorFocus
```

跨 tab 的 Reedit 分发由 `MainWindowViewModel` 连接，不让 History ViewModel 直接持有 Prompt Editor ViewModel。

加载与刷新策略：

- workspace 为空时不启动扫描。
- T03 先接通 workspace 切换后的立即扫描，这是 T03 到 T05 的唯一读文件触发方式。
- T06 再接通固定间隔重新扫描。
- 搜索框只过滤当前已加载的 sessions。

### 4.5 接通 View

将占位 Border 替换为真实列表：

- 搜索框双向绑定 `SearchText`。
- 列表绑定 `Sessions`。
- 每行按钮绑定 `ResumeCommand` / `ReeditInitialPromptCommand`。
- 空态显示 `No sessions for current workspace`。
- 不提供手动 `Refresh` 按钮。

具体列表字段和视觉布局单独作为布局工作条目确认。

## 5. 任务拆分

按“先确定 UI，再用可验证的业务纵切同步补基础能力”的顺序推进。每个业务功能的基础能力随该功能一起实现，避免先做一批无法从界面验证的底层代码。

- [x] **4-T01 确认 History Sessions UI 布局**：先单独确定列表字段、列宽/换行策略、行内 `Resume` / `Reedit` 按钮位置、搜索框位置、加载态、空态、错误态和缩放表现；本任务只产出布局决策，不改业务逻辑。
- [x] **4-T02 用 mock 数据落地 UI 骨架**：重组 `HistorySessionInfo` 为 `Id / WorkspacePath / CreatedAt / LastUpdatedAt / InitialPrompt / LastPrompt / LastReply / SessionFilePath`，同步 `MockHistorySessionReader`，接通搜索框、列表、空态和行内按钮占位；此时仍使用 mock reader，先验证 UI 和绑定形态。
- [x] **4-T03 接通真实历史列表读取纵切**：为“筛出当前 workspace 的真实 session 并形成最小可用列表”同时实现所需基础能力，包括删除 `SessionHistoryRootPath`、新增 `MaxSessionHistoryFiles=100`、在 `CodexSessionHistoryReader` 内定位 `$CODEX_HOME/sessions` / `~/.codex/sessions`、按最新文件限制枚举、实现 Windows/WSL 路径等价、读取 `session_meta` 并解析 `Id`、`WorkspacePath`、`CreatedAt`、`SessionFilePath`、`LastUpdatedAt`。其中 `Id`、`WorkspacePath`、`CreatedAt` 来自 `session_meta.payload.id/cwd/timestamp`，`SessionFilePath` 来自当前 JSONL 文件路径，`LastUpdatedAt` 来自文件内最后一条有效记录的 `timestamp`；`InitialPrompt`、`LastPrompt`、`LastReply` 在本任务中允许先填空字符串。最后在 `App.axaml.cs` 切换到真实 reader，并只接通 `SetWorkspacePath(...)` 触发的一次性加载，不实现定时刷新；验收点是选择或切换 workspace 后列表能显示真实 session 数量、id、workspace、创建时间和更新时间。
- [ ] **4-T04 补齐列表展示字段解析纵切**：为“列表展示可读上下文”在 T03 的真实 session 基础上补齐文本字段解析，只处理 `InitialPrompt`、`LastPrompt`、`LastReply` 三个字段和对应坏文件跳过策略；不再改 history root 定位、路径等价、文件枚举限制、`Id`、`WorkspacePath`、`CreatedAt`、`LastUpdatedAt`、`SessionFilePath` 的解析边界。验收点是 UI 中能看到真实初始 prompt、最后 prompt、最后回复，缺少文本字段时按本任务规则降级或跳过，且不会拖垮整个列表。
- [ ] **4-T05 接通搜索业务纵切**：将 `SessionHistoryService` 搜索范围改为只过滤已加载 session 的 `InitialPrompt`，保持搜索框即时过滤；验收点是搜索不会重新扫描文件，且不会匹配 `Id`、`WorkspacePath`、`LastPrompt`、`LastReply`。
- [ ] **4-T06 接通定时刷新纵切**：为“workspace 不变时历史列表自动更新”实现固定间隔重新扫描；workspace 切换后的立即加载沿用 T03 已完成能力，搜索只作用于当前已加载结果，不提供手动 `Refresh` 按钮；验收点是不切换 workspace，仅等待定时周期也能发现新增或变化的 session 文件。
- [ ] **4-T07 接通 Resume 纵切**：为“从历史列表恢复 session”同时实现 `SessionLifecycleService.Resume(session, currentWorkspacePath, config)`、Terminal 启动参数、行内 `ResumeCommand` 和 Footer 状态同步；验收点是点击行内 `Resume` 会在当前 workspace 执行 `codex resume <session-id>`。
- [ ] **4-T08 接通 Reedit Initial Prompt 纵切**：为“重新编辑初始 prompt”同时实现 History 到 Prompt Editor 的跨 tab 分发、填充 `InitialPrompt`、切换到 Prompt Editor 页签；验收点是点击 `Reedit` 只填回 prompt，不自动 launch，也不执行 `codex fork`。
- [ ] **4-T09 构建与人工验收**：构建项目，选择 workspace 后按 UI 布局、真实列表、字段展示、搜索、定时刷新、Resume、Reedit、错误态逐项验收。

## 6. 错误处理决策

错误处理随对应业务纵切一起实现，不单独放到最后统一收敛。每个任务交付时必须同时覆盖正常路径和本节列出的失败路径。

### 6.1 UI 骨架错误态

执行工作项：`4-T02`

- mock 数据为空时显示空态，不显示异常状态。
- 行内 `Resume` / `Reedit` 按钮在占位阶段不执行真实业务，点击不能抛未处理异常。
- UI 需要预留加载态、空态、错误态的承载位置，后续任务只填充状态，不重排整体布局。

### 6.2 真实列表读取错误

执行工作项：`4-T03`

- 当前 workspace 为空时不扫描 Codex 历史目录，列表显示空态。
- `$CODEX_HOME/sessions` 和 `~/.codex/sessions` 都不存在时返回空列表，不作为致命错误。
- 单个 JSONL 文件无法打开、不是合法 JSONL、缺少 `session_meta`、缺少 resume key、缺少 `cwd`、缺少 created timestamp、缺少 last updated timestamp 时，跳过该文件。
- 单个文件解析失败不影响其他文件展示。
- 如果存在跳过文件，Footer 显示简短结果，例如 `Loaded 12 sessions, skipped 3 invalid files.`。
- T03 不处理 `InitialPrompt`、`LastPrompt`、`LastReply` 缺失问题，这三个字段允许暂时为空字符串。

### 6.3 文本字段解析错误

执行工作项：`4-T04`

- `InitialPrompt` 缺失或无法识别时，先使用空字符串，不跳过整个 session；真实展示效果不好再调整策略。
- `LastPrompt` 缺失或无法识别时使用空字符串。
- `LastReply` 缺失或无法识别时使用空字符串。
- 文本字段解析失败不能影响 T03 已经可展示的基础列表字段。
- 文本字段提取只读取用户 prompt 和 assistant reply，不解析 tool call 细节。

### 6.4 搜索边界

执行工作项：`4-T05`

- 搜索文本为空时返回当前已加载的完整列表。
- 非空搜索只匹配 `InitialPrompt`。
- `InitialPrompt` 为空的 session 不匹配非空 query。
- 搜索不触发重新扫描文件，也不改变已加载 session 缓存。

### 6.5 定时刷新错误

执行工作项：`4-T06`

- 定时扫描失败时保留上一轮可用列表，不清空 UI。
- 定时扫描中的单文件解析失败继续按 T03/T04 规则跳过。
- 定时刷新失败只更新 Footer 状态，不弹模态框。
- 定时刷新不能打断当前搜索文本；刷新后继续用当前 `SearchText` 过滤新加载结果。

### 6.6 Resume 错误

执行工作项：`4-T07`

- 当前 workspace 为空属于编程错误，正常操作路径不会出现；`SessionLifecycleService.Resume(...)` 应抛异常，不作为可恢复 UI 状态处理。
- session id 为空属于编程错误，正常操作路径不会出现；`SessionLifecycleService.Resume(...)` 应抛异常，不作为可恢复 UI 状态处理。
- `codex resume <session-id>` 的 Terminal 启动失败时返回失败结果并更新 Footer。
- Resume 不把进程加入 Ongoing Sessions，留到阶段 5 处理。

### 6.7 Reedit 错误

执行工作项：`4-T08`

- `InitialPrompt` 为空时，`Reedit` 按钮应禁用或点击后在 Footer 显示明确状态；具体采用哪种由 T08 实现时按 UI 效果决定。
- 跨 tab 分发失败时只更新 Footer 状态，不自动 launch。
- `Reedit` 不执行 `codex fork`，也不启动 Terminal。

## 7. 验收标准

- `AppConfig` 不再包含 `SessionHistoryRootPath`。
- `CodexSessionHistoryReader` 内部固定使用 `$CODEX_HOME/sessions` / `~/.codex/sessions` 定位历史文件。
- 当前 workspace 为空时不展示全局历史。
- 选择 `D:\work\HaloCreek` 时，可以匹配 Codex JSONL 中 `/mnt/d/work/HaloCreek` 的历史 session。
- 默认只解析最新的至多 100 个 session 文件。
- 历史列表按 `LastUpdatedAt` 倒序排列。
- 搜索只按 `InitialPrompt` 过滤已加载 session。
- 缺少 resume key、created timestamp 或 last updated timestamp 的 session 文件会被视为解析失败。
- 单个坏 session 文件不会导致整个页面不可用。
- 点击 `Resume` 会在当前 workspace 拉起 Terminal，并执行对应 session 的 resume。
- 点击 `Reedit` 会把 `InitialPrompt` 放回 Prompt Editor，且不会自动 launch。
- History ViewModel 不直接解析 JSONL、不直接调用 `Process.Start`、不直接持有 Avalonia 控件。
- `CodexSessionHistoryReader` 是唯一知道 Codex JSONL 文件结构的模块。

## 8. 执行记录

### 2026-05-20：完成 4-T02 mock UI 骨架

- `HistorySessionInfo` 已重组为 `Id / WorkspacePath / CreatedAt / LastUpdatedAt / InitialPrompt / LastPrompt / LastReply / SessionFilePath`，并删除 `Title`、`InitialPromptPreview`、`State` 和 `HistorySessionState`。
- `MockHistorySessionReader` 已同步新模型字段，继续作为当前 History Sessions 页签的数据来源。
- `HistorySessionsView.axaml` 已从静态占位行改为绑定 `Sessions` 的列表；搜索框绑定 `SearchText`；空态绑定 `IsEmptyStateVisible`；加载态和错误态保留为后续任务承载位。
- 行内 `Resume` / `Reedit` 按钮已接入占位命令，点击不执行真实业务，也不会抛未处理异常；真实行为留给 `4-T07` 和 `4-T08`。
- `SessionHistoryService.SearchSessions(...)` 已按后续反馈更名为 `GetFilteredSessions(...)`，当前过滤逻辑保持为只匹配 `InitialPrompt`。
- 已执行构建验证：`dotnet build HaloCreek/HaloCreek.csproj` 通过，结果为 `0 个警告`、`0 个错误`。

### 2026-05-20：完成 4-T03 真实历史列表读取纵切

- `AppConfig` 已删除 `SessionHistoryRootPath`，新增 `MaxSessionHistoryFiles`，默认值为 `100`；配置合并支持正整数覆盖。
- `CodexSessionHistoryReader` 已接入真实 JSONL 读取：优先 `$CODEX_HOME/sessions`，再 fallback 到 `~/.codex/sessions`；只枚举 `.jsonl`，按文件更新时间取最新的至多 `MaxSessionHistoryFiles` 个文件。
- 已实现 `session_meta.payload.id/cwd/timestamp` 解析，`SessionFilePath` 使用当前 JSONL 文件路径，`LastUpdatedAt` 使用文件内最后一条有效记录的顶层 `timestamp`。
- 已实现 Windows/WSL workspace 路径等价，例如 `D:\work\HaloCreek` 可匹配 `/mnt/d/work/HaloCreek`；该能力放在 `PlatformInfrastructure`。
- 单个 JSONL 文件无法打开、格式非法或缺少必需字段时会跳过该文件，不影响其他 session；跳过数量会通过 Footer 状态展示。
- `InitialPrompt`、`LastPrompt`、`LastReply` 在本任务保持空字符串，留给 `4-T04` 补齐。
- `App.axaml.cs` 已从 `MockHistorySessionReader` 切换到 `CodexSessionHistoryReader`。
- `HistorySessionsViewModel` 已改为 workspace 切换时加载一次真实列表；搜索只过滤已加载的内存列表，不会重新扫描文件。
- 已执行构建验证：`dotnet build HaloCreek/HaloCreek.csproj` 通过，结果为 `0 个警告`、`0 个错误`。
