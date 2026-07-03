# SessionStateView Markdown 展示需求和组件调研

## 背景

当前 `SessionStateView` 的消息正文使用普通文本展示。session 消息里经常出现 Markdown 风格内容，例如标题、引用、列表、加粗、链接、代码片段和简单表格。纯文本展示会降低可读性，也不方便从应用内直接打开消息里提到的文件。

本条目只讨论 `SessionStateView` 消息正文展示，不改变 session JSONL 解析的数据边界。session reader 仍然只负责读取原始消息文本，Markdown 渲染和链接行为放在 View/UI 层处理。

## 需求

### 必须支持

- 基本 Markdown 展示：
  - 标题。
  - 引用块。
  - 加粗。
  - 条目列表。
  - 链接。
  - 行内代码和代码块。
- 字符级选中和复制：
  - 用户应能拖选正文中的任意字符范围。
  - 复制结果应尽量接近原文语义，不能只能整段复制。
- 链接点击：
  - 本地文件链接优先于网页链接。
  - 本地文件只要求能“以默认应用打开”。
  - 网页链接可作为次要能力支持。
- 窄面板适配：
  - 不能因为 Markdown 渲染导致 `SessionStateView` 横向撑开。
  - 长文本需要正常换行。
  - 代码块可以横向滚动，但不应影响整个消息流布局。

### 可以降级

- 表格不强求。
  - 当前 message 展示区域较窄，表格即使完整渲染也可能不好看。
  - 首版可以按普通文本、等宽文本或简化表格展示。
- 图片、HTML、Mermaid、LaTeX 不作为首版目标。
  - session 消息展示主要服务工作流文本阅读，不应因为非核心能力引入复杂渲染和安全边界。

## 链接行为

链接需要区分本地文件和网页：

- 本地文件：
  - 支持 Markdown 链接：`[label](path)`。
  - 支持绝对路径，例如 Windows 路径 `D:\work\file.txt`、WSL 路径 `/mnt/d/work/file.txt`。
  - 相对路径是否支持需要结合当前 workspace root 定义，首版可以只支持绝对路径。
  - 点击后调用平台默认打开能力，例如 Avalonia `TopLevel.Launcher.LaunchFileAsync` 或等价封装。
- 网页链接：
  - 支持 `http://` 和 `https://`。
  - 点击后用默认浏览器打开。
- 不应默认打开任意自定义 scheme。
  - 例如 `cmd:`, `powershell:`, `file:` 等需要明确评估后再支持。

如果选用现成 Markdown 组件，需要确认是否能拦截链接点击；如果组件默认直接打开 URI，应包装或配置链接处理，确保本地文件优先和 scheme 白名单可控。

## 候选组件

### LiveMarkdown.Avalonia

NuGet 包：`LiveMarkdown.Avalonia`

优点：

- 面向 LLM/AI 输出的实时 Markdown 渲染，场景接近 session 消息。
- README 明确支持 selectable text，并说明支持跨 Markdown 元素选中。
- 支持链接、表格、代码块语法高亮。
- 2.2.0 包含 `net10.0` target，依赖 Avalonia 12.0.0；当前 HaloCreek 使用 Avalonia 12.0.3，版本方向匹配。
- 许可证为 Apache-2.0，主风险不是 copyleft，而是需要保留 license/notice。

风险和待验证：

- 默认能力较多，包括图片、远程图片、SVG、Mermaid、LaTeX 扩展等。首版应只启用文本 Markdown 能力，避免扩大安全和依赖边界。
- 需要验证链接点击是否可完全接管，以实现本地文件优先和默认应用打开。
- 需要验证在 `ListBox` 消息流中多条 renderer 的性能和选中体验。
- 需要验证窄面板下代码块、长链接和表格的布局行为。

初步结论：

首选 spike 对象。它最明确满足“字符级选中和复制”这个硬要求。

### Avalonia.Controls.Markdown

NuGet 包：`Avalonia.Controls.Markdown`

优点：

- 包 owner 为 AvaloniaUI，NuGet verified。
- 版本有 12.x，和当前 Avalonia 主版本方向一致。
- 目标是 Avalonia Markdown rendering control。

风险和待验证：

- 依赖 `Avalonia.Controls.Documents` 和 `Avalonia.Controls.Documents.Serialization.Html`，组件栈更重。
- 需要确认字符级选中和复制能力是否满足消息流场景。
- 需要确认链接点击是否可接管，尤其是本地文件链接。
- 可能引入额外 licensing 包，需要单独确认发布影响。

初步结论：

备选。除非 `LiveMarkdown.Avalonia` 链接接管或布局表现不满足，否则不优先使用。

### Markdown.Avalonia

NuGet 包：`Markdown.Avalonia`

优点：

- 社区老牌 Avalonia Markdown 控件。
- 支持 Markdown 渲染，依赖和用法相对成熟。
- MIT license。

风险和待验证：

- 稳定版主要停在 11.0.3，12.x 目前是 prerelease。
- 当前没有明确证据表明它支持跨 Markdown 元素的字符级选中和复制。
- 对 HaloCreek 当前 Avalonia 12.0.3 的适配需要实测。

初步结论：

不作为首选。除非前两个方案都不可用，再考虑。

### 自研轻量 Markdown 渲染

做法：

- 自己解析标题、引用、列表、加粗、链接、代码块等简单语法。
- 使用 `SelectableTextBlock` 或自定义控件渲染。

优点：

- 可控性最高。
- 可以精确实现本地文件链接优先和 scheme 白名单。
- 不引入第三方 Markdown 控件。

风险：

- 字符级选中和链接点击在同一段文本内同时满足并不简单。
- 跨块选中、复制、代码块滚动、链接命中测试都需要额外实现。
- 容易把一个 UI 小需求做成长期维护成本。

初步结论：

不作为首版推荐。只有现成组件无法满足链接接管或选中复制要求时，再考虑自研局部渲染。

## 推荐路径

1. 先做 `LiveMarkdown.Avalonia` spike。
2. 在 `SessionStateView` 中只替换消息正文展示，不调整 session reader 和 ViewModel 数据结构。
3. 引入组件后先关闭或不启用图片、HTML、Mermaid、LaTeX 等非目标能力。
4. 验证：
   - 任意字符范围拖选和复制。
   - 跨标题、引用、列表、代码块的选择行为。
   - 本地文件链接点击后用默认应用打开。
   - 网页链接点击后用默认浏览器打开。
   - 窄面板下长链接、代码块和表格不撑坏布局。
   - 多条消息滚动性能。
5. 如果链接点击无法接管或本地文件链接体验不可控，再评估 `Avalonia.Controls.Markdown`。
6. 如果现成组件都不能满足字符级选中和本地文件链接，最后再考虑自研控件。

## 非目标

- 不做完整 Markdown 编辑器。
- 不做富文本编辑。
- 不做复杂表格布局优化。
- 不渲染远程图片。
- 不执行或预览 HTML。
- 不支持任意 URI scheme。
