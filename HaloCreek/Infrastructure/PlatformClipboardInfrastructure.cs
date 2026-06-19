using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using HaloCreek.Logging;

namespace HaloCreek.Infrastructure
{
    public sealed record ClipboardTextSnapshot(
        string Text,
        DateTimeOffset CapturedAt);

    public sealed class ClipboardTextChangedEventArgs : EventArgs
    {
        public ClipboardTextChangedEventArgs(ClipboardTextSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ClipboardTextSnapshot Snapshot { get; }
    }

    // 当前没有业务入口使用剪贴板监听；基础设施先保留，后续确认有真实入口再重新接入。
    public sealed class PlatformClipboardInfrastructure : IDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        private readonly IClipboard _clipboard;
        private readonly DispatcherTimer _timer;
        private bool _isDisposed;
        private bool _isReading;
        private string? _lastText;

        public PlatformClipboardInfrastructure(Window owner)
        {
            ArgumentNullException.ThrowIfNull(owner);

            _clipboard = owner.Clipboard
                ?? throw new InvalidOperationException("Clipboard API is not available.");
            _timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, OnTimerTick);
            _timer.Start();
        }

        public event EventHandler<ClipboardTextChangedEventArgs>? TextChanged;

        public Task<string?> GetTextAsync()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(PlatformClipboardInfrastructure));
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                return _clipboard.TryGetTextAsync();
            }

            return Dispatcher.UIThread.InvokeAsync(() => _clipboard.TryGetTextAsync());
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
        }

        private async void OnTimerTick(object? sender, EventArgs e)
        {
            if (_isDisposed || _isReading)
            {
                return;
            }

            _isReading = true;
            try
            {
                var text = await GetTextAsync();
                if (_isDisposed)
                {
                    return;
                }

                if (text is null)
                {
                    _lastText = null;
                    return;
                }

                if (string.Equals(_lastText, text, StringComparison.Ordinal))
                {
                    return;
                }

                _lastText = text;
                var snapshot = new ClipboardTextSnapshot(text, DateTimeOffset.Now);
                TextChanged?.Invoke(this, new ClipboardTextChangedEventArgs(snapshot));
            }
            catch (Exception ex)
            {
                if (!_isDisposed)
                {
                    Log.Warning("Clipboard", "Clipboard read failed.");
                    Log.Debug("Clipboard", ex.ToString());
                }
            }
            finally
            {
                _isReading = false;
            }
        }
    }
}
