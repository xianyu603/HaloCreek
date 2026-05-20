# Status Bar Notes

短期不严肃设计 status bar，不引入统一状态仲裁、优先级队列或状态事件模型。

当前 Footer / status bar 只作为轻量反馈入口，优先回应用户直接操作，例如选择 workspace、Launch、后续 Resume / Reedit。

workspace 切换触发的 History/Git/Ongoing 等副作用加载默认静默，不为了诊断信息抢占 Footer。后台定时任务也先保持克制，除非失败会影响用户判断，否则不主动改 Footer。

等后续有余力时，再统一设计 status bar 的产品规则和技术结构，例如区分用户输入反馈、后台任务、错误、进度和 tab 内诊断信息。
