using System;
using Avalonia.Threading;
using HaloCreek.Logging;

namespace HaloCreek.Services
{
    public sealed class GlobalHotkeyActionService
    {
        private readonly FloatingPromptService _floatingPromptService;

        public GlobalHotkeyActionService(FloatingPromptService floatingPromptService)
        {
            _floatingPromptService = floatingPromptService
                ?? throw new ArgumentNullException(nameof(floatingPromptService));
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
    }
}
