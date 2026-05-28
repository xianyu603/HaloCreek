# MVP1 Boundary And Status Wrap-Up Notes

* ConfigService 不应在配置 JSON 或读取失败时静默回退，应返回可展示或可记录的配置错误。
* TmuxService 的 Launch/Exit 不应 fire-and-forget 到完全无反馈，异步失败至少要能回到 session 状态或 footer。
* Footer 不需要立刻做复杂仲裁，但应把普通信息、错误、后台刷新和用户直接操作反馈区分开。

