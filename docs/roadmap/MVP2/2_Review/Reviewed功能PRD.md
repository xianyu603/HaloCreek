# Reviewed 功能 PRD

## 目标

提供一个轻量级的文件级审阅进度标记能力，类似 Git staging area，但不占用真实 Git index。用户可以把当前已审阅认可的文件内容标记为 `Reviewed`，后续 Agent 或手动修改会重新出现在 `Unreviewed` 中，便于继续审阅和回退对比。

## 范围

### 本期支持

- 文件级 `Mark Reviewed`
- 文件级 `Mark Unreviewed`
- 三个文件视图：
  - `Modified`: `WorkingTree vs HEAD`
  - `Unreviewed`: `WorkingTree vs Reviewed`
  - `Reviewed`: `Reviewed vs HEAD`
- 外部 diff：
  - `WorkingTree vs HEAD`
  - `WorkingTree vs Reviewed`
  - `Reviewed vs HEAD`
- `Refresh`
- commit 后自动清理已进入 HEAD 的 reviewed 状态
- Reviewed 状态随 `HaloCreekIndex` 保留，应用重启后可继续使用

### 本期不支持

- hunk 级 reviewed
- 删除、重命名、冲突、copy 文件进入 `Unreviewed` 工作流
- 内置 diff viewer
- 自动区分 Agent 修改和用户手动修改

## 核心概念

### Reviewed

`Reviewed` 表示用户当前已审阅认可的文件内容。

对未执行过 `Mark Reviewed` 的文件，`Reviewed` 默认等于 `HEAD`。对已执行过 `Mark Reviewed` 的文件，`Reviewed` 等于当时保存下来的文件内容。

实现上使用独立的 Git index 文件 `HaloCreekIndex` 承载 reviewed 内容，不使用真实 Git staging area。

```text
Reviewed(path) = HaloCreekIndex:path ?? HEAD:path
```

### Unreviewed 视图

仅包含 `Modified`、`Added`、`Untracked` 类型文件。

用于显示 working tree 相对 `Reviewed` 的新修改。当文件满足以下条件时显示：

```text
WorkingTree:path != Reviewed(path)
```

### Reviewed 视图

用于显示 `Reviewed` 相对 `HEAD` 的修改。当文件满足以下条件时显示：

```text
HaloCreekIndex has reviewed entry for path
AND Reviewed(path) != HEAD:path
```

同一个文件可以同时出现在 `Reviewed` 和 `Unreviewed` 中，表示“已有审阅认可内容，但后续又产生了新修改”。

## 用户操作

### Modified 视图

显示当前 Git 修改，即 `WorkingTree vs HEAD`。

操作复用现有 Git tab：

- 文件右键：文件操作 popup
- 空白右键：路径操作 popup、`Refresh`
- 双击文件：执行现有默认文件动作

### Unreviewed 视图

显示尚未审阅的新修改。

文件操作：

- `Mark File Reviewed`
- `Diff Against Reviewed`

空白操作：

- `Refresh`

双击：

- 默认执行 `Diff Against Reviewed`

### Reviewed 视图

显示已审阅但尚未进入 HEAD 的内容。

文件操作：

- `Mark File Unreviewed`
- `Diff Against Working Tree`
- `Diff Against HEAD`

空白操作：

- `Refresh`

双击：

- 默认执行 `Diff Against HEAD`

## 状态清理规则

`Refresh` 时执行清理：

1. 如果文件不再是 modified，清理 `HaloCreekIndex` 中该文件的 reviewed 状态。
2. 如果 `HaloCreekIndex` 中该文件内容已经等于 `HEAD:path`，清理该文件的 reviewed 状态。

这表示用户 commit 后，已被接受的 reviewed 状态会自动消失。

## 实现说明

Review 相关 Git 操作使用独立 index 文件，不直接读写真实 `.git/index`。

```text
GIT_INDEX_FILE=<workspace git dir>/HaloCreekIndex
```

- `HaloCreekIndex` 是 `Reviewed` 的物理存储。
- `Mark Reviewed` 将文件当前 working tree 内容写入 `HaloCreekIndex`。
- `Mark Unreviewed` 清理该文件在 `HaloCreekIndex` 中的 reviewed 状态，使 `Reviewed` 回到 `HEAD`。
- `WorkingTree vs Reviewed` 从 working tree 和 `HaloCreekIndex` 分别取文件内容，写入临时文件后交给外部 diff 工具展示。
- `Reviewed vs HEAD` 从 `HaloCreekIndex` 和 `HEAD` 分别取文件内容，写入临时文件后交给外部 diff 工具展示。
- 只有 review 相关命令注入 `GIT_INDEX_FILE`；现有 Git tab 和用户真实 staging area 不受影响。
- `HaloCreekIndex` 落在 workspace 对应 Git 目录下，因此应用重启后可以恢复上次关闭时的 `Reviewed` 状态。

## 约束与边界

- `Mark Unreviewed` 的语义是清理该文件在 `HaloCreekIndex` 中的 reviewed 状态，使 `Reviewed` 回到 `HEAD`。
- 删除、重命名、冲突、copy 等结构性变更只在 `Modified` 视图展示，不进入 `Unreviewed`。
- diff 通过生成临时左右文件并调用外部 diff 工具实现。
