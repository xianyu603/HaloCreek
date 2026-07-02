using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using HaloCreek.Logging;

namespace HaloCreek.Infrastructure
{
    public sealed class GlobalHotkeyRegistrar : IDisposable
    {
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(20);

        private const int MainWindowHotkeyId = 1;
        private const uint WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint VirtualKeyH = 0x48;
        private const string MainWindowHotkeyText = "Ctrl+Alt+H";

        private NativeMessageWindow? _messageWindow;
        private readonly DispatcherTimer _retryTimer;
        private bool _mainWindowHotkeyRegistered;
        private bool _isDisposed;

        public GlobalHotkeyRegistrar()
        {
            _retryTimer = new DispatcherTimer(RetryInterval, DispatcherPriority.Background, OnRetryTimerTick);
        }

        public event EventHandler? MainWindowHotkeyPressed;

        public void RegisterDefaultHotkeys()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(GlobalHotkeyRegistrar));
            }

            if (!OperatingSystem.IsWindows())
            {
                Log.Warning(
                    "GlobalHotkey",
                    "Global hotkey registration skipped because the current platform is not Windows.");
                return;
            }

            TryRegisterPendingHotkeys();
            if (!AreAllHotkeysRegistered)
            {
                _retryTimer.Start();
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _retryTimer.Stop();
            _retryTimer.Tick -= OnRetryTimerTick;

            if (_mainWindowHotkeyRegistered && _messageWindow is not null)
            {
                if (!UnregisterHotKey(_messageWindow.Handle, MainWindowHotkeyId))
                {
                    Log.Warning(
                        "GlobalHotkey",
                        $"Failed to unregister main window hotkey. Hotkey={MainWindowHotkeyText}");
                }

                _mainWindowHotkeyRegistered = false;
            }

            _messageWindow?.Dispose();
            _messageWindow = null;
        }

        private bool AreAllHotkeysRegistered =>
            _mainWindowHotkeyRegistered;

        private void OnRetryTimerTick(object? sender, EventArgs e)
        {
            if (_isDisposed)
            {
                return;
            }

            TryRegisterPendingHotkeys();
            if (AreAllHotkeysRegistered)
            {
                _retryTimer.Stop();
            }
        }

        private void TryRegisterPendingHotkeys()
        {
            if (_messageWindow is null)
            {
                try
                {
                    _messageWindow = new NativeMessageWindow(HandleWindowMessage);
                }
                catch (Exception ex)
                {
                    Log.Error("GlobalHotkey", ex, "Failed to create global hotkey message window.");
                    return;
                }
            }

            if (!_mainWindowHotkeyRegistered)
            {
                _mainWindowHotkeyRegistered = TryRegisterHotkey(
                    MainWindowHotkeyId,
                    ModControl | ModAlt,
                    VirtualKeyH,
                    MainWindowHotkeyText,
                    "main window");
            }
        }

        private bool TryRegisterHotkey(
            int hotkeyId,
            uint modifiers,
            uint virtualKey,
            string hotkeyText,
            string hotkeyName)
        {
            if (_messageWindow is null)
            {
                throw new InvalidOperationException("Global hotkey message window is not available.");
            }

            if (RegisterHotKey(_messageWindow.Handle, hotkeyId, modifiers, virtualKey))
            {
                Log.Info("GlobalHotkey", $"Registered {hotkeyName} hotkey. Hotkey={hotkeyText}");
                return true;
            }

            var exception = new Win32Exception(Marshal.GetLastWin32Error());
            Log.Error(
                "GlobalHotkey",
                exception,
                $"Failed to register {hotkeyName} hotkey. Hotkey={hotkeyText}");
            return false;
        }

        private IntPtr HandleWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == WmHotkey && wParam.ToInt32() == MainWindowHotkeyId)
            {
                Log.Info("GlobalHotkey", $"Main window hotkey pressed. Hotkey={MainWindowHotkeyText}");
                MainWindowHotkeyPressed?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
            }

            return DefWindowProc(hwnd, message, wParam, lParam);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private sealed class NativeMessageWindow : IDisposable
        {
            private const int HwndMessage = -3;

            private readonly NativeWindowProcedure _windowProcedure;
            private readonly string _className;
            private readonly IntPtr _moduleHandle;

            public NativeMessageWindow(NativeWindowProcedure windowProcedure)
            {
                _windowProcedure = windowProcedure ?? throw new ArgumentNullException(nameof(windowProcedure));
                _className = $"HaloCreekGlobalHotkeyWindow_{Environment.ProcessId}_{Guid.NewGuid():N}";
                _moduleHandle = GetModuleHandle(null);

                var windowClass = new WindowClassEx
                {
                    Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                    WindowProcedure = _windowProcedure,
                    Instance = _moduleHandle,
                    ClassName = _className,
                };

                if (RegisterClassEx(ref windowClass) == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                Handle = CreateWindowEx(
                    0,
                    _className,
                    string.Empty,
                    0,
                    0,
                    0,
                    0,
                    0,
                    new IntPtr(HwndMessage),
                    IntPtr.Zero,
                    _moduleHandle,
                    IntPtr.Zero);

                if (Handle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            public IntPtr Handle { get; }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero && !DestroyWindow(Handle))
                {
                    Log.Warning("GlobalHotkey", "Failed to destroy global hotkey message window.");
                }

                if (!UnregisterClass(_className, _moduleHandle))
                {
                    Log.Warning("GlobalHotkey", "Failed to unregister global hotkey message window class.");
                }
            }
        }

        private delegate IntPtr NativeWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClassEx
        {
            public uint Size;
            public uint Style;
            public NativeWindowProcedure WindowProcedure;
            public int ClassExtraBytes;
            public int WindowExtraBytes;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr BackgroundBrush;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? MenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string ClassName;
            public IntPtr SmallIcon;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int extendedStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parentWindow,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool UnregisterClass(string className, IntPtr instance);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? moduleName);
    }
}
