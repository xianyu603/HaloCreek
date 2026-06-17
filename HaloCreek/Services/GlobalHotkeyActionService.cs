using System;
using Avalonia.Controls;
using Avalonia.Threading;
using HaloCreek.Logging;

namespace HaloCreek.Services
{
    public sealed class GlobalHotkeyActionService
    {
        private readonly Window _mainWindow;
        private readonly SessionLifecycleService _sessionLifecycleService;

        public GlobalHotkeyActionService(
            Window mainWindow,
            SessionLifecycleService sessionLifecycleService)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _sessionLifecycleService = sessionLifecycleService
                ?? throw new ArgumentNullException(nameof(sessionLifecycleService));
        }

        public void ActivateMainWindow()
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_mainWindow.WindowState == WindowState.Minimized)
                    {
                        _mainWindow.WindowState = WindowState.Normal;
                    }

                    _mainWindow.Show();
                    _mainWindow.Activate();
                }
                catch (Exception ex)
                {
                    Log.Error("GlobalHotkey", ex, "Failed to activate main window from global hotkey.");
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
