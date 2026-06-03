# MVP2 Review 基础能力实施方案

## 1. 目标

实现 Reviewed 功能的底层基础能力，让 Review 面板可以围绕文件级审阅进度提供三个列表和外部 diff：

- `Modified`：`WorkingTree vs HEAD`，复用现有 Git tab 的 Git 状态能力。
- `Unreviewed`：`WorkingTree vs Reviewed`，显示用户上次标记 reviewed 之后又产生的新修改。
- `Reviewed`：`Reviewed vs HEAD`，显示已经审阅认可、但尚未进入 HEAD 的内容。

本方案只实现文件级 reviewed，不实现 hunk 级 reviewed，不处理删除、重命名、冲突、copy 文件进入 Review 工作流。

## 2. 设计边界

### 2.1 Reviewed 的存储语义

`Reviewed(path)` 表示用户当前已经审阅认可的文件内容：

```text
Reviewed(path) = HaloCreekIndex:path ?? HEAD:path
```

物理存储使用独立 Git index 文件：

```text
GIT_INDEX_FILE=<workspace git dir>/HaloCreekIndex
```

约束：

- Review 相关命令才允许注入 `GIT_INDEX_FILE`。
- 现有 Git tab、真实 Git staging area 和用户 `.git/index` 不受影响。
- `HaloCreekIndex` 只作为 reviewed 快照存储，不引入额外 store。
- `HaloCreekIndex` 中的 entry 只代表“该 path 有显式 reviewed 快照”；没有 entry 时 fallback 到 `HEAD:path`。

### 2.2 模块边界

新增两个平铺服务：

- `ReviewSnapshotService`：拥有 reviewed 快照的 Git plumbing、路径校验、临时 reviewed 文件创建和清理规则。
- `DiffService`：拥有启动外部 diff 工具的逻辑。第一版先写死 diff 命令，不接配置系统。

不把 `GIT_INDEX_FILE`、`git show`、`git update-index` 泄漏到服务调用方。
HEAD 内容临时文件不属于 ReviewSnapshotService，归入 `GitService` 的通用 Git 对象读取能力。

### 2.3 数据返回形态

本阶段列表只返回相对路径。后续接入层如果需要状态、行数、图标、更新时间，再扩展模型。

先定义一个很薄但语义明确的模型，避免把普通字符串在服务和后续接入层之间混用：

```csharp
public sealed record ReviewFilePath(string RelativePath);
```

## 3. 当前基础

已有能力：

- `GitService.GetChanges(...)` 已能读取 `git status --porcelain=v1 -z --untracked-files=all`。
- `GitChangeInfo` 已包含相对路径、变更类型、staged 状态和原路径。
- `AppConfig` 有 `DiffToolPath` 字段，但本阶段 `DiffService` 先不依赖它。

缺口：

- 没有 reviewed 快照存储服务。
- 没有 `WorkingTree vs Reviewed` 和 `Reviewed vs HEAD` 的文件列表查询。
- 没有为 reviewed 内容生成临时文件的能力。
- `GitService` 缺少为 `HEAD:path` 生成临时文件的能力。
- 没有 Review 专用 diff 启动入口。

## 4. ReviewSnapshotService

### 4.1 建议职责

新增文件：

```text
HaloCreek/Services/ReviewSnapshotService.cs
```

职责：

- 定位 workspace 对应 Git 目录和 `HaloCreekIndex` 路径。
- 管理所有带 `GIT_INDEX_FILE=HaloCreekIndex` 的 git 命令。
- 将文件当前 working tree 内容写入 reviewed 快照。
- 删除指定文件的 reviewed 快照。
- 按业务规则清理无效 reviewed 快照。
- 返回 `Reviewed vs HEAD` 文件列表。
- 返回 `WorkingTree vs Reviewed` 文件列表。
- 为 diff 创建 reviewed 内容对应的临时文件。

不负责：

- 上层接入的选中项、命令状态、错误展示。
- 外部 diff 工具启动。
- HEAD 内容临时文件创建。
- 真实 Git staging area。
- hunk 级状态。

### 4.2 建议 API

第一版接口建议：

```csharp
public sealed class ReviewSnapshotService
{
    public ReviewOperationResult MarkFileReviewed(string? workspacePath, string? relativePath);

    public ReviewOperationResult MarkFileUnreviewed(string? workspacePath, string? relativePath);

    public ReviewOperationResult RefreshReviewSnapshot(string? workspacePath);

    public ReviewFileListResult GetReviewedAgainstHeadFiles(string? workspacePath);

    public ReviewFileListResult GetWorkingTreeAgainstReviewedFiles(string? workspacePath);

    public ReviewTempFileResult CreateTempReviewedFile(string? workspacePath, string? relativePath);
}
```

结果模型建议：

```csharp
public sealed record ReviewOperationResult(bool Succeeded, string Message);

public sealed record ReviewFileListResult(
    IReadOnlyList<string> RelativePaths,
    string Message);

public sealed record ReviewTempFileResult(
    bool Succeeded,
    string? TempFilePath,
    string Message);
```

边界：

- `workspacePath` 为空、无效、不是 Git repo：返回失败结果，不抛到调用方。
- `relativePath` 为空、绝对路径、包含越界路径：返回失败结果。
- Git 命令失败：返回失败结果并保留 stderr 摘要。
- 不做“猜接口”式 fallback；取不到外部协议字段或 Git 对象时，按空、失败或显式报告处理。

### 4.3 GitService 扩展

HEAD 文件读取是通用 Git 能力，不带 Review 语义，放在 `GitService`：

```csharp
public GitTempFileResult CreateTempHeadFile(string? workspacePath, string? relativePath);
```

建议结果模型：

```csharp
public sealed record GitTempFileResult(
    bool Succeeded,
    string? TempFilePath,
    string Message);
```

职责：

- 为 `HEAD:path` 创建临时文件。
- 如果 `HEAD:path` 不存在，创建空文件并返回成功。
- 不读取 `HaloCreekIndex`。
- 不注入 `GIT_INDEX_FILE`。
- 路径校验沿用 `GitService` / workspace relative path 规则。

### 4.4 Git repo 与 index 路径

所有入口先规范化 workspace：

1. `Path.GetFullPath(workspacePath.Trim())`
2. `Directory.Exists(...)`
3. `git -C <workspace> rev-parse --git-dir`

`rev-parse --git-dir` 返回相对路径时，按 workspace 拼成绝对路径。

`HaloCreekIndex` 路径：

```text
Path.Combine(gitDir, "HaloCreekIndex")
```

注意：

- 不写到 `.HaloCreek`，因为 `GIT_INDEX_FILE` 更适合跟随 Git dir。
- worktree / submodule 的 git dir 可能不是 `<workspace>/.git`，必须以 `rev-parse --git-dir` 为准。

### 4.5 路径校验

ReviewSnapshotService 只接受 workspace relative path。

校验规则：

- 不允许空路径。
- 不允许绝对路径。
- 不允许 `..` 越出 workspace。
- 统一把 `\` 转成 `/` 传给 Git。
- filesystem 访问时再使用 `Path.Combine(workspacePath, relativePath)`。

本阶段不处理 rename 的旧路径和新路径联动；遇到 rename/copy/conflicted 时只在 `Modified` 展示，不进入 `Unreviewed` / `Reviewed` 操作流。

## 5. 核心行为

### 5.1 MarkFileReviewed

语义：

把指定文件当前 working tree 内容写入 `HaloCreekIndex`，作为新的 reviewed 快照。

规则：

- 要求文件在 working tree 中存在。
- 删除、重命名、copy、冲突状态不支持。
- 如果 `WorkingTree:path == HEAD:path`，no-op，不写 reviewed 快照。
- 对 untracked / added 文件，`HEAD:path` 不存在，working tree 存在即视为不同。
- 成功后该文件可能同时出现在：
  - `Reviewed`：如果 reviewed 内容相对 HEAD 有变化。
  - `Unreviewed`：如果之后 working tree 又继续变化。

Git 命令：

```text
GIT_INDEX_FILE=<HaloCreekIndex> git -C <workspace> update-index --add -- <relativePath>
```

比较 `WorkingTree:path == HEAD:path` 时，优先用 blob hash 语义比较，而不是简单文本读取：

- working tree blob：`git -C <workspace> hash-object --path=<relativePath> -- <absoluteWorkingTreePath>`
- head blob：`git -C <workspace> rev-parse HEAD:<relativePath>`

如果 `HEAD:<relativePath>` 不存在，按“不相等”处理。

### 5.2 MarkFileUnreviewed

语义：

删除指定文件在 `HaloCreekIndex` 中的 reviewed 快照，使 `Reviewed(path)` 回到 `HEAD:path`。

规则：

- 如果 `HaloCreekIndex` 不存在或没有该 path entry，no-op。
- 如果 reviewed 快照已经等于 HEAD，该状态本身是待清理状态；可当作 cleanup/no-op，不向用户报错。
- 删除 entry 后不修改 working tree，不修改真实 Git index。

Git 命令：

```text
GIT_INDEX_FILE=<HaloCreekIndex> git -C <workspace> update-index --force-remove -- <relativePath>
```

如果 `HaloCreekIndex` 不存在，直接返回成功 no-op，避免为了删除创建空 index 文件。

### 5.3 RefreshReviewSnapshot

语义：

根据 PRD 清理 `HaloCreekIndex` 中无效或已进入 HEAD 的 reviewed 快照。

清理规则：

1. 文件不再是 modified / added / untracked 时，清理 reviewed 快照。
2. reviewed 快照内容已经等于 `HEAD:path` 时，清理 reviewed 快照。
3. 文件进入 deleted / renamed / copied / conflicted 状态时，清理 entry，并在日志里记录原因。

实现步骤：

1. 枚举 `HaloCreekIndex` 中的 entry。
2. 调用 `GitService.GetChanges(workspacePath)` ，得到当前 Git 状态。
3. 为每个 reviewed entry 判断是否仍属于 Review 支持的变更类型。
4. 比较 reviewed blob 与 HEAD blob。
5. 对需要清理的 path 执行 `update-index --force-remove`。

注意：

- 不要用 `git diff --cached HEAD` 直接比较整个 `HaloCreekIndex`，因为 `HaloCreekIndex` 是 overlay index，不是完整 HEAD index；直接 diff 会把未写入 overlay 的 HEAD 文件误判为删除。
- `Refresh` 是显式清理入口，不在每次查询里偷偷修改 index。后续接入层可以在用户触发刷新时按顺序执行 `RefreshReviewSnapshot` 再刷新列表。

### 5.4 GetReviewedAgainstHeadFiles

语义：

返回所有 `Reviewed(path) != HEAD:path` 的路径。

候选集：

- 只枚举 `HaloCreekIndex` 中存在 entry 的 path。

判断：

- reviewed blob id 来自 `HaloCreekIndex` entry。
- head blob id 来自 `HEAD:path`。
- `HEAD:path` 不存在时，reviewed entry 视为相对 HEAD 有变化。
- reviewed blob id 等于 head blob id 时，不返回；该状态可由 `RefreshReviewSnapshot` 清理。

Git 命令：

```text
GIT_INDEX_FILE=<HaloCreekIndex> git -C <workspace> ls-files -s -z
git -C <workspace> rev-parse HEAD:<relativePath>
```

返回列表按 path case-insensitive 排序，保持调用方展示稳定。

### 5.5 GetWorkingTreeAgainstReviewedFiles

语义：

返回所有 `WorkingTree(path) != Reviewed(path)` 的路径。

候选集：

- 从当前 Git status 中取 `Modified`、`Added`、`Untracked`。
- 排除 `Deleted`、`Renamed`、`Copied`、`Conflicted`、`Unknown`。

判断：

- working tree 文件不存在时不进入列表。
- reviewed blob 优先来自 `HaloCreekIndex:path`。
- 如果 `HaloCreekIndex:path` 不存在，则 fallback 到 `HEAD:path`。
- 如果 reviewed 和 HEAD 都不存在，则 reviewed 侧按空文件处理；这主要用于未 reviewed 的 untracked 文件 diff。

比较方式：

- working tree：`git hash-object --path=<relativePath> -- <absoluteWorkingTreePath>`
- reviewed entry：从 `HaloCreekIndex` 的 `ls-files -s -z` 取 blob id。
- HEAD fallback：`git rev-parse HEAD:<relativePath>`。

返回列表同样按 path case-insensitive 排序。

### 5.6 CreateTempReviewedFile

语义：

为指定 path 创建一个临时文件，内容为当前 `Reviewed(path)`。

规则：

- 一定创建临时文件并返回 path。
- 如果 `HaloCreekIndex:path` 存在，写入 reviewed blob 内容。
- 如果 reviewed entry 不存在，则调用 `GitService.CreateTempHeadFile(...)`，让 Reviewed fallback 到 HEAD。

Git 命令：

```text
GIT_INDEX_FILE=<HaloCreekIndex> git -C <workspace> show :<relativePath>
```

reviewed entry 不存在时不要在 ReviewSnapshotService 内直接 `git show HEAD:<relativePath>`；HEAD 临时文件由 `GitService` 创建。

### 5.7 GitService.CreateTempHeadFile

语义：

为指定 path 创建一个临时文件，内容为 `HEAD:path`。

规则：

- 一定创建临时文件并返回 path。
- 如果 `HEAD:path` 不存在，创建空文件。
- 用于 `Reviewed vs HEAD` 和 untracked 的 head 侧 diff。
- 不读取 `HaloCreekIndex`。
- 不带 Review 语义。

建议 Git 命令：

```text
git -C <workspace> show HEAD:<relativePath>
```

## 6. 临时文件管理

外部 diff 工具启动后可能异步读取文件，因此第一版不做临时文件清理，不在 `Process.Start()` 返回后删除，也不在应用退出时统一删除。

临时文件创建位置建议：

```text
%TEMP%/HaloCreek/review-diff/
```

规则：

- `GitService.CreateTempHeadFile(...)` 和 `ReviewSnapshotService.CreateTempReviewedFile(...)` 各自创建自己语义下的临时文件。
- 文件名包含 side、短随机串和原文件名，便于 diff 标题识别。
- 本阶段不记录本进程创建的临时文件列表。
- 本阶段不新增 `ReviewTempFileService`。
- 实现时在创建临时文件的位置加注释，说明当前策略是“为了避免外部 diff 工具异步读取失败，暂不清理临时文件”，并说明未来可以演进为应用启动/退出时清理过期 `review-diff` 文件。

语义边界仍需保持：ReviewSnapshotService 只创建 Reviewed 语义临时文件，GitService 创建 HEAD 语义临时文件。

## 7. DiffService

### 7.1 目标

新增文件：

```text
HaloCreek/Services/DiffService.cs
```

第一版只负责传入两个文件路径后启动外部 diff。

建议 API：

```csharp
public sealed class DiffService
{
    public ReviewOperationResult OpenDiff(string leftPath, string rightPath, string title);
}
```

### 7.2 第一版命令

先写死使用 TortoiseGitProc：

```text
TortoiseGitProc.exe /command:diff /path:<rightPath> /path2:<leftPath>
```

命名约定：

- left：旧内容，例如 HEAD 或 Reviewed。
- right：新内容，例如 Reviewed 或 WorkingTree。

如果本机没有 TortoiseGitProc，返回明确失败，由后续接入层决定如何展示。

暂不实现：

- diff tool 配置。
- 多 diff 工具适配。
- 内置 diff viewer。
- 临时文件清理策略，包括等待 diff 进程退出后删除、应用退出时清理或启动时清理过期文件。

后续可以再把 `DiffToolPath` 接进 `DiffService`，或复用 Git file browser 的配置动作模型。

## 8. 任务拆分

- [ ] **R-01 新增 ReviewSnapshotService 基础骨架**：实现 workspace 校验、git dir 定位、`HaloCreekIndex` 路径解析、统一 git 命令执行和结果模型。
- [ ] **R-02 实现 reviewed entry 枚举和 blob 查询**：支持 `ls-files -s -z` 读取 `HaloCreekIndex` entry，支持 `HEAD:path` / `:path` blob 查询。
- [ ] **R-03 实现 MarkFileReviewed / MarkFileUnreviewed**：用独立 `GIT_INDEX_FILE` 写入或删除单文件 reviewed 快照。
- [ ] **R-04 实现 RefreshReviewSnapshot**：按支持类型和 reviewed==HEAD 规则清理无效 entry。
- [ ] **R-05 实现两个文件列表查询**：返回 `Reviewed vs HEAD` 和 `WorkingTree vs Reviewed` 路径列表。
- [ ] **R-06 扩展 GitService 的 HEAD 临时文件能力**：为 `HEAD:path` 创建 temp 文件，支持 empty head 文件，不读取 `HaloCreekIndex`。
- [ ] **R-07 实现 ReviewSnapshotService 的 Reviewed 临时文件能力**：为 reviewed 内容创建 temp 文件；没有 reviewed entry 时调用 `GitService.CreateTempHeadFile(...)`。
- [ ] **R-08 新增 DiffService**：传入左右文件路径启动写死 diff 命令。
- [ ] **R-09 构建与 code review**：编译通过，并做服务边界、Git index 隔离、路径安全和失败路径的 code review。

## 9. Code Review 关注点

- `Mark File Reviewed` 会把当前 working tree 文件内容写入 `<git dir>/HaloCreekIndex`。
- `Mark File Reviewed` 不改变真实 Git staging area。
- `WorkingTree == HEAD` 时执行 `Mark File Reviewed` 是 no-op。
- untracked 文件执行 `Mark File Reviewed` 后，会出现在 `Reviewed` 中。
- reviewed 文件被 Agent 或用户继续修改后，会同时出现在 `Reviewed` 和 `Unreviewed` 中。
- `Mark File Unreviewed` 会删除该文件 reviewed 快照，使 `Reviewed(path)` 回到 `HEAD:path`。
- `Refresh` 会清理已经等于 HEAD 的 reviewed entry。
- commit 后点击 `Refresh`，已进入 HEAD 的 reviewed 状态消失。
- `Unreviewed` 只包含 modified / added / untracked，不包含 deleted / renamed / copied / conflicted。
- `Reviewed` 只包含 `HaloCreekIndex` 中相对 HEAD 有变化的 path。
- `Diff Against Reviewed` 能打开 working tree 与 reviewed 临时文件的 diff。
- `Diff Against HEAD` 能打开由 `GitService` 创建的 head 临时文件与 reviewed 临时文件的 diff。
- untracked 文件与 head diff 时，head 侧使用空文件。
- 外部 diff 工具缺失、Git 命令失败、非 Git workspace 都有明确失败消息。

## 10. 构建建议

### 10.1 编译

```powershell
dotnet build HaloCreek/HaloCreek.csproj
```

如果在 WSL 环境中构建，继续使用本仓库既有 Windows dotnet 构建流程。

## 11. 暂不实现

- hunk 级 reviewed。
- 删除、重命名、copy、conflict 的 reviewed 工作流。
- 内置 diff viewer。
- diff tool 配置化。
- reviewed 快照的额外数据库 / store。
- 自动区分 Agent 修改和用户手动修改。
- reviewed 文件列表的额外元信息。
- Review UI / ViewModel 接入。
