using System;
using Avalonia.Controls;

namespace HaloCreek.Infrastructure
{
    public sealed class ApplicationWindowFocusTracker : IDisposable
    {
        private readonly Window _window;
        private bool _hasFocus = true;
        private bool _isDisposed;

        public ApplicationWindowFocusTracker(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
        }

        public bool HasFocus => _hasFocus;

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
        }

        private void OnWindowActivated(object? sender, EventArgs e)
        {
            _hasFocus = true;
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            _hasFocus = false;
        }
    }
}
