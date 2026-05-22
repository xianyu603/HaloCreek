using System;

namespace HaloCreek.Services
{
    public sealed class TmuxService
    {
        public event EventHandler<TmuxSessionStateChangedEventArgs>? StateChanged;

        public string Launch(TmuxLaunchRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            return "halocreek-pending-" + Guid.NewGuid().ToString("N")[..8];
        }

        public void Exit(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        }

        public TerminalCommandSpec GetFrontCommand(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            return new TerminalCommandSpec(
                string.Empty,
                "tmux",
                new[] { "attach-session", "-t", identifier });
        }

        public void StartWatching(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        }

        public void StopWatching(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        }

        private void OnStateChanged(TmuxSessionStateChangedEventArgs args)
        {
            StateChanged?.Invoke(this, args);
        }
    }
}
