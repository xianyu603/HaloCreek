# MVP1 Boundary And Status Wrap-Up Notes

* History 面板的 loading/error/empty 文案可以保持静态，但显隐应由 VM 的真实加载状态驱动。
* History 当前只可靠表达有数据和无数据，无法区分首次加载、加载失败、未选 workspace、搜索无结果和真实空历史。
* ConfigService 不应在配置 JSON 或读取失败时静默回退，应返回可展示或可记录的配置错误。
* TmuxService 的 Launch/Exit 不应 fire-and-forget 到完全无反馈，异步失败至少要能回到 session 状态或 footer。
* Footer 不需要立刻做复杂仲裁，但应把普通信息、错误、后台刷新和用户直接操作反馈区分开。

