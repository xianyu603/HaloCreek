# HaloCreek

HaloCreek 是一个面向个人 Agent 编程工作流的 Codex shell wrapper。它把 workspace、prompt、Codex session、历史记录、Git 变更和人工审阅入口放到一个轻量桌面应用里，用来降低 Human in the loop 场景里的重复操作成本，当前主要面向 Windows + WSL + Codex CLI 的本地开发环境。

## 项目动机

Codex CLI 已经适合在终端里执行具体编程任务，但真实的开发流程通常不只是一条 prompt 到一次输出。开发者需要反复切换 workspace、整理上下文、拉起或恢复 session、查看 Git 变更、把审阅意见继续发送给 Agent，并在终端、文件管理器、Git 工具和历史记录之间来回移动。

这些动作本身不复杂，但在 Human in the loop 的使用方式里会频繁出现。HaloCreek 的目标是把这些低价值的重复操作收束到一个本地桌面入口中，让开发者仍然保留对 Codex CLI、Git、编辑器和终端的直接控制，同时减少围绕它们的机械操作。

从定位上看，HaloCreek 更接近一个本地工作流壳层，而不是 IDE 插件、通用终端管理器或云端 Agent 平台。它优先服务个人开发者的可见、可控、可打断的 Agent 编程过程。

## 做什么

- 提供一个本地桌面入口，集中管理 workspace、prompt、session、history、Git 变更和审阅上下文。
- 以 Codex CLI 为实际执行核心，围绕它补足个人工作流中缺失的操作面。
- 支持开发者在 Agent 执行、人工审阅、补充追问和 Git 检查之间快速切换。
- 优先优化 Windows + WSL 环境下的本地开发体验。
- 保持轻量边界，尽量使用现有工具和本地文件系统能力。

## 不做什么

- 不做完整的多 Agent 任务编排、队列调度或自动化流水线。
- 不替代 IDE，也不试图接管代码编辑、语言服务或调试体验。
- 不把 Codex 内部状态作为主控制面；HaloCreek 只在必要处读取外部可观察的信息。
- 不做云端协作平台、账号系统、远程执行平台或团队级权限管理。
- 不追求一开始覆盖所有操作系统、终端和 Git 客户端组合。

## 文档

- [Feature Guide](docs/FeatureGuide.md)
- [Startup Guide](docs/StartupGuide.md)

开发设计规则见 [Designing Rules](docs/DesigningRules.md)。

## License

MIT. See [LICENSE](LICENSE).
