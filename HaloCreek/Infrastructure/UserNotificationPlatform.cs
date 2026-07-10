using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.WinUI.Notifications;
using HaloCreek.Logging;

namespace HaloCreek.Infrastructure
{
    public sealed record UserNotificationRequest(
        string Title,
        string Message,
        string? ActivationPayload = null);

    public sealed record UserNotificationActivatedEventArgs(string? Payload);

    public sealed class UserNotificationPlatform
    {
        private const string LogCategory = "Notification";
        private const string AppUserModelId = "HaloCreek";
        private const string ActivationPayloadArgument = "payload";
        private static UserNotificationPlatform? _currentPlatform;

        private readonly Window _owner;

        public UserNotificationPlatform(Window owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _currentPlatform = this;
            EnsureNotificationsRegistered();
        }

        public event EventHandler<UserNotificationActivatedEventArgs>? Activated;

        public Task SendAsync(UserNotificationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Message);

            if (!OperatingSystem.IsWindows())
            {
                Log.Warning(LogCategory, "User notification skipped because Windows notification APIs are unavailable.");
                return Task.CompletedTask;
            }

            try
            {
                FlashTaskbarIcon();
                var builder = new ToastContentBuilder()
                    .AddText(request.Title)
                    .AddText(request.Message);

                if (!string.IsNullOrWhiteSpace(request.ActivationPayload))
                {
                    builder.AddArgument(
                        ActivationPayloadArgument,
                        request.ActivationPayload);
                }

                builder.Show();
                Log.Info(LogCategory, "User notification sent.");
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, $"User notification failed. Error={ex.Message}");
                Log.Debug(LogCategory, ex.ToString());
            }

            return Task.CompletedTask;
        }

        private void ActivateOwner()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ActivateOwner);
                return;
            }

            if (_owner.WindowState == WindowState.Minimized)
            {
                _owner.WindowState = WindowState.Normal;
            }

            if (!_owner.IsVisible)
            {
                _owner.Show();
            }

            _owner.Activate();

            if (OperatingSystem.IsWindows())
            {
                var handle = _owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle != IntPtr.Zero)
                {
                    SetForegroundWindow(handle);
                }
            }
        }

        private void PublishActivated(string? payload)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => PublishActivated(payload));
                return;
            }

            ActivateOwner();
            Activated?.Invoke(this, new UserNotificationActivatedEventArgs(payload));
        }

        private void FlashTaskbarIcon()
        {
            var handle = _owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var flashInfo = new FlashWindowInfo
            {
                Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
                Handle = handle,
                Flags = FlashWindowFlags.Caption | FlashWindowFlags.Tray | FlashWindowFlags.TimerNoFG,
                Count = 3,
                TimeoutMilliseconds = 0,
            };

            FlashWindowEx(ref flashInfo);
        }

        private static void EnsureNotificationsRegistered()
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        }

        private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat args)
        {
            _currentPlatform?.PublishActivated(ReadActivationPayload(args.Argument));
        }

        private static string? ReadActivationPayload(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return null;
            }

            var expectedPrefix = ActivationPayloadArgument + "=";
            if (!arguments.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            return WebUtility.UrlDecode(arguments[expectedPrefix.Length..]);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool FlashWindowEx(ref FlashWindowInfo flashInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct FlashWindowInfo
        {
            public uint Size;
            public IntPtr Handle;
            public FlashWindowFlags Flags;
            public uint Count;
            public uint TimeoutMilliseconds;
        }

        [Flags]
        private enum FlashWindowFlags : uint
        {
            Stop = 0,
            Caption = 0x00000001,
            Tray = 0x00000002,
            Timer = 0x00000004,
            TimerNoFG = 0x0000000C,
        }
    }
}
