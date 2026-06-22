# Skill 选择体验实施方案

## 背景

本方案参考 `docs/roadmap/0.2/5.skill/skill选择体验方案.md`，目标是在现有 prompt 自动补全框架上接入 `$` skill 补全。

当前代码已经具备：

- `PromptInputViewModel` 负责补全触发、键盘导航、多级菜单、接受写回。
- `CompletionCoordinator` 按触发字符路由到不同 `ICompletionSource`。
- `ShortcutPhraseCompletionSource` 已作为 `#` 快捷语数据源注册。
- `PromptCompletionItem` 已支持 `Title`、`Description`、`InsertText`、`Children`，可以承载来源、分类和 skill 的三级展示。

因此首版不改补全 View 和 ViewModel 的交互模型，只新增 skill 数据读取、分类和 `$` 数据源。

## 设计边界

### 首版做

- 新增 `$` 触发字符，注册 `SkillCompletionSource`。
- 读取可验证的本地 `SKILL.md` 文件，构建 skill 候选。
- 支持空 query 的 `来源 -> 分类 -> skill` 浏览。
- 支持非空 query 的来源精确匹配、分类精确匹配、skill 名称精确匹配和模糊匹配。
- 同名 skill 不合并，说明里展示来源和短路径。
- 接受具体 skill 时写回 `$skill-name`，并复用现有追加空格规则。

### 首版不做

- 不读取或猜测 Codex 内部不可见 skill 注册表。
- 不发明 `$source/skill`、`$plugin:skill` 等新语法。
- 不做 skill 安装、禁用、详情页、跳转文件。
- 不扩展 `PromptCompletionItem` 结构，来源、分类、路径先压缩进 `Description`。
- 不改自动补全菜单样式和键盘行为。

如果某类来源无法可靠识别，不静默并入 `Project`、`System` 或 `User`，而是进入 `Other`。

## 目录读取策略

新增 `SkillCatalogReader`，只负责从明确目录读取 skill 元数据，返回稳定的内存模型。

首版候选目录：

| 来源 | 目录 | 说明 |
| --- | --- | --- |
| `Project` | 当前 workspace 下 `.codex/skills/*/SKILL.md` | HaloCreek 当前已有项目级 skill。 |
| `System` | `HOME/.codex/skills/.system/*/SKILL.md` | System默认路径。 |
| `User` | `HOME/.codex/skills/*/SKILL.md` | User默认路径。 |
| `Other` | 其他后续明确暴露的可读目录 | 只有能确认存在且可读、但无法归入已知来源时才纳入。 |

首版来源按上表只保留 `Project`、`System`、`User`、`Other`。不提前预留 `Admin`、`Plugin` 等枚举值；后续确认稳定来源和读取路径后，再按真实需求增加。

读取规则：

1. `Project` 扫描当前 workspace 下 `.codex/skills/*/SKILL.md`。
2. `System` 扫描 `HOME/.codex/skills/.system/*/SKILL.md`。
3. `User` 扫描 `HOME/.codex/skills/*/SKILL.md`，但跳过 `.system` 目录，避免和 `System` 重复。
4. `Other` 只接入后续明确暴露的可读目录；首版如果没有明确目录，可以不产生 `Other` 候选。
5. 文件不存在、目录不可读或单个文件解析失败时跳过该项，并记录 warning；不影响其他 skill。
6. 只读取 frontmatter 中的 `name` 和 `description`。
7. `name` 为空时，使用 skill 目录名；目录名也为空则跳过。
8. `description` 为空时，说明中只展示来源和短路径。

## 元数据解析

新增模型放在 `HaloCreek/Services/Completions` 或 `HaloCreek/Models`：

```csharp
internal sealed record SkillCatalogItem(
    string Name,
    string? Description,
    SkillSourceKind Source,
    string ShortPath);

internal enum SkillSourceKind
{
    Project,
    System,
    User,
    Other,
}
```

frontmatter 解析保持最小实现：

- 仅识别文件开头 `---` 到下一行 `---` 的 YAML-like 区块。
- 仅解析单行 `name:` 和 `description:`。
- 不支持多行 YAML、数组、复杂转义。
- 解析不到字段就留空或使用目录名，不猜测。

这样满足当前 skill 文件格式，也避免为了完整 YAML 引入额外依赖。

## 分类策略

新增 `SkillCategoryClassifier`，输入 `SkillCatalogItem`，输出固定分类：

- `信息获取`
- `代码编写`
- `动作执行`
- `管理配置`
- `其他`

分类可以启发式，首版使用名称、描述和路径关键字匹配：

| 分类 | 关键词示例 |
| --- | --- |
| `信息获取` | `docs`、`documentation`、`search`、`read`、`api`、`framework` |
| `代码编写` | `code`、`coding`、`implement`、`refactor` |
| `动作执行` | `build`、`test`、`run`、`launch`、`install`、`publish`、`generate`、`asset` |
| `管理配置` | `skill`、`plugin`、`create`、`creator`、`config`、`install`、`manage` |

匹配大小写不敏感。多分类命中时按上表顺序取第一个。没有命中进入 `其他`。

## `$` 补全源

新增 `SkillCompletionSource : ICompletionSource`。

职责：

- 构造时读取一次 skill catalog，并在应用生命周期内复用该快照。
- 根据 query 构建 `PromptCompletionItem` 列表。
- 同步一次性返回结果即可，接口仍保持 `IAsyncEnumerable<CompletionQuerySnapshot>`。

触发字符：

```csharp
public const char TriggerCharacter = '$';
```

注册位置：

```csharp
var completionCoordinator = new CompletionCoordinator(
    new Dictionary<char, ICompletionSource>
    {
        [ShortcutPhraseCompletionSource.TriggerCharacter] = new ShortcutPhraseCompletionSource(),
        [SkillCompletionSource.TriggerCharacter] = new SkillCompletionSource(appCommonRuntime),
    });
```

`FloatingPromptService` 和主 prompt 共享同一个 `CompletionCoordinator`，注册一次即可覆盖两个入口。

## 展示生成规则

### 空 query

返回来源一级菜单。

```text
Project
  信息获取
    avalonia-api-docs
  动作执行
    halocreek-build-review
```

实现方式：

1. 按固定来源顺序分组：`Project`、`System`、`User`、`Other`。
2. 每个来源项 `InsertText = null`，`Children` 是分类项。
3. 每个分类项 `InsertText = null`，`Children` 是具体 skill。
4. 具体 skill `InsertText = "$" + skill.Name`。

如果某来源下没有 skill，不展示该来源。

### 精确匹配来源

当 query 精确匹配来源名称或别名时，一级列表直接展示该来源下的分类。

来源别名建议：

| 来源 | 别名 |
| --- | --- |
| `Project` | `project`、`workspace` |
| `System` | `system`、`builtin` |
| `User` | `user`、`personal` |
| `Other` | `other` |

### 精确匹配分类

当 query 精确匹配分类名称或别名时，一级列表展示所有来源下该分类的具体 skill。

分类别名建议：

| 分类 | 别名 |
| --- | --- |
| `信息获取` | `info`、`docs` |
| `代码编写` | `code`、`review` |
| `动作执行` | `run`、`build` |
| `管理配置` | `manage`、`config` |
| `其他` | `other` |

如果来源别名和分类别名冲突，来源优先。

### 精确匹配 skill 名称

当 query 精确匹配一个或多个 skill 名称时：

1. 精确匹配 skill 放在列表前部。
2. 同名项全部展示。
3. 再追加其余模糊匹配项，避免用户输入完整名称后看不到相关结果。
4. 接受任一同名项仍写回相同 `$skill-name`。

### 模糊匹配

未精确匹配来源、分类或 skill 名称时，在全部具体 skill 中做模糊匹配。

匹配字段：

- skill 名称。
- skill 描述。
- 来源名称。
- 分类名称。
- 短路径。

排序：

1. 名称命中。
2. 描述命中。
3. 来源、分类或路径命中。
4. 同组内按来源固定顺序，再按 skill 名称稳定排序。

## `PromptCompletionItem` 映射

具体 skill item：

```csharp
new PromptCompletionItem
{
    Title = skill.Name,
    Description = FormatSkillDescription(skill),
    InsertText = "$" + skill.Name,
}
```

说明格式：

```text
Project · 信息获取 · .codex/skills/avalonia-api-docs · Find version-matched Avalonia API documentation...
```

当描述过长时先不截断，交给现有 UI `TextTrimming="CharacterEllipsis"` 处理。生成说明时不写完整绝对路径，避免菜单泄露过长本机路径。

## 刷新和性能

首版在 `SkillCompletionSource` 构造时读取一次 skill 目录，并把读取结果作为应用运行期内的固定快照。应用运行中新增、删除或修改的 skill 可以暂时不感知，直到应用重启后重新读取。

`StartQuery` 只基于内存快照做匹配和排序，不再涉及文件 IO。结果按一过性快照返回：每次 query 只产出一个 `CompletionQuerySnapshot`，不需要为了“边读边展示”做分批次返回。

首版不做后台文件监听、不做目录 last write time 检查，也不做运行期手动刷新入口。

## 文件改动清单

预计新增：

- `HaloCreek/Services/Completions/SkillCompletionSource.cs`
- `HaloCreek/Services/Completions/SkillCatalogReader.cs`
- `HaloCreek/Services/Completions/SkillCatalogItem.cs`
- `HaloCreek/Services/Completions/SkillCategoryClassifier.cs`

预计修改：

- `HaloCreek/App.axaml.cs`：注册 `$` skill completion source。

不需要修改：

- `PromptInputViewModel.cs`
- `PromptInputView.axaml`
- `PromptCompletionItem.cs`
- `CompletionCoordinator.cs`

除非实现中发现现有模型无法表达首版行为，否则不要扩展这些公共补全模型。

## 实施步骤

### Step 1：数据结构、目录读取和可开关诊断日志

新增 skill catalog 的核心数据结构和读取框架，但暂不接入 `$` 补全 UI。

改动范围：

- 新增 `SkillCatalogItem` 和 `SkillSourceKind`, 放到同一个实现文件。
- 新增 `SkillCatalogReader`，按 `Project`、`System`、`User`、`Other` 的既定目录策略扫描 `SKILL.md`。
- 实现 frontmatter 最小解析，只读取单行 `name:` 和 `description:`。
- 暂不引入真实分类器，所有读取到的 skill 在后续 source 使用时先归入 `其他`。
- 单个目录不存在、不可读或单个 skill 解析失败时记录 warning 并跳过，不影响其他项。

为了让该步骤可以单独验证，增加 Debug 等级的诊断日志：

- 读取完成后用 Debug 日志打印每个来源的数量，以及每个 skill 的 `Source`、`Name`、`ShortPath`。
- 日常是否展示由日志等级控制，不额外增加环境变量或配置开关。
- warning 日志仍使用 warning 等级，因为它表示实际读取失败。
- 该诊断日志可长期保留；后续如果日志量增加，再统一接入分级存储或日志过滤策略。

本步骤验收点：

- 构建通过。
- 启用 Debug 日志展示后，启动应用可以在日志中看到当前 workspace、system、user skill 的扫描摘要。
- 非 Debug 日志展示下，不显示 skill catalog 明细。
- 不改 `CompletionCoordinator` 注册，不触发 `$` 补全行为变化。

### Step 2：最小 `$` completion source 接入

新增 `SkillCompletionSource` 并注册 `$` 触发字符，先形成可见闭环。

改动范围：

- `SkillCompletionSource` 构造时读取一次 skill catalog，并在应用生命周期内复用快照。
- 在 `App.axaml.cs` 的 `CompletionCoordinator` 注册 `[SkillCompletionSource.TriggerCharacter]`。
- 实现空 query 的 `来源 -> 其他 -> skill` 三级菜单。
- 具体 skill item 的 `InsertText` 为 `$` + skill name，复用现有接受补全和追加空格行为。
- 具体 skill 的说明展示来源、固定分类 `其他`、短路径和描述。

本步骤验收点：

- `$` 可以触发补全。
- 空 query 可以看到按来源分组的三级菜单。
- 接受具体 skill 后写回 `$skill-name `。
- 非空 query 可以先返回空列表或使用最简单的名称包含匹配，但不要在本步骤实现完整优先级规则。

### Step 3：补齐查询匹配和排序规则

在分类仍固定为 `其他` 的前提下，补齐非空 query 的业务规则。

改动范围：

- 来源精确匹配：query 命中来源名称或别名时，展示该来源下的分类和 skill。
- 分类精确匹配：query 命中 `其他` 或别名时，展示所有来源下该分类的具体 skill。
- skill 名称精确匹配：精确命中的同名 skill 放在列表前部，并追加其余模糊匹配项。
- 模糊匹配：匹配 skill 名称、描述、来源名称、分类名称和短路径。
- 排序规则按名称命中、描述命中、来源/分类/路径命中分组；同组内按来源固定顺序和 skill 名称稳定排序。

本步骤验收点：

- `project`、`system`、`user` 等来源 query 可以定位对应来源。
- `other` 可以定位固定分类下的所有 skill。
- 输入完整 skill 名称时，同名项全部展示在前部。
- 输入描述、路径或来源关键字时，可以出现相关模糊结果。

### Step 4：接入启发式分类器

新增 `SkillCategoryClassifier`，将 Step 2 和 Step 3 中的固定 `其他` 替换为启发式分类结果。

改动范围：

- 分类器根据 skill 名称、描述和短路径做大小写不敏感关键词匹配。
- 分类输出限定在 `信息获取`、`代码编写`、`动作执行`、`管理配置`、`其他`。
- 多分类命中时按方案中的分类顺序取第一个。
- 未命中时进入 `其他`。
- 更新空 query 三级菜单和分类精确匹配逻辑，使其使用真实分类。

本步骤验收点：

- 空 query 中可以看到真实分类分组，而不是所有 skill 都在 `其他`。
- `docs`、`code`、`build`、`config` 等分类别名可以命中对应分类。
- 分类变化不影响具体 skill 的写回文本，仍为 `$skill-name `。

## 风险和降级

| 风险 | 处理 |
| --- | --- |
| Codex 后续新增来源没有稳定可读路径 | 首版不展示这些来源；不猜测，不提前加枚举。 |
| Windows App 读取 WSL `HOME` 路径失败 | 仍展示 Project skill；System/User skill 缺失时打 warning。 |
| frontmatter 不是简单单行格式 | 使用目录名作为 `name`，描述留空；解析失败只跳过单项。 |
| 应用运行中新增或修改 skill 后补全不更新 | 首版接受该限制，重启应用后重新读取。 |
| 同名 skill 写回后无法消歧 | 按行为方案保留 `$skill-name`，只在说明中暴露冲突。 |

## 完成标准

- `$` 触发 skill 补全，并复用现有自动补全键盘行为。
- 空 query 可以按 `来源 -> 分类 -> skill` 浏览。
- 非空 query 可以快速定位来源、分类或具体 skill。
- 具体 skill 接受后写回 `$skill-name `。
- 无法可靠识别来源时不误标，进入 `Other` 或不展示。
- 构建通过；若没有测试项目，最终说明手工验收路径。
