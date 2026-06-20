using System;
using Avalonia.Threading;
using HaloCreek.Logging;

namespace HaloCreek.Services
{
    public sealed class GlobalHotkeyActionService
    {
        private readonly FloatingPromptService _floatingPromptService;
        private readonly SessionLifecycleService _sessionLifecycleService;

        public GlobalHotkeyActionService(
            FloatingPromptService floatingPromptService,
            SessionLifecycleService sessionLifecycleService)
        {
            _floatingPromptService = floatingPromptService
                ?? throw new ArgumentNullException(nameof(floatingPromptService));
            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
        }

        public void ActivateFloatingPrompt()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    _floatingPromptService.ShowOrActivate();
                }
                catch (Exception ex)
                {
                    Log.Error("GlobalHotkey", ex, "Failed to activate floating prompt from global hotkey.");
                }
            });
        }

        public void ActivateFrontTerminal()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // 已知这里会有点卡顿 但是似乎没啥快速解决的办法 要探测前台就必须走tmux 又不想维护两份前台记录 未来还是考虑重构一下 将tmux和terminal搞到一起 走一份账本
                    _sessionLifecycleService.ActivateFrontClient();
                }
                catch (Exception ex)
                {
                    Log.Error("GlobalHotkey", ex, "Failed to activate front terminal from global hotkey.");
                }
            });
        }
    }
}
