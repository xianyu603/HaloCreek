using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.Notifications;
using HaloCreek.Logging;
using Avalonia.Controls;

namespace HaloCreek.Infrastructure
{
    public sealed record UserNotificationRequest(
        string Title,
        string Message);

    public sealed class UserNotificationPlatform
    {
        private const string LogCategory = "Notification";
        private const string AppUserModelId = "HaloCreek";

        private readonly Window _owner;

        public UserNotificationPlatform(Window owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            EnsureNotificationsRegistered();
        }

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
                new ToastContentBuilder()
                    .AddText(request.Title)
                    .AddText(request.Message)
                    .Show();
                Log.Info(LogCategory, "User notification sent.");
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, $"User notification failed. Error={ex.Message}");
                Log.Debug(LogCategory, ex.ToString());
            }

            return Task.CompletedTask;
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

        private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat _)
        {
            // 点击toast行为在这里接
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

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
