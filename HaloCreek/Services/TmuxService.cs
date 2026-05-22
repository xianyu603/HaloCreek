using System;

namespace HaloCreek.Services
{
    public sealed class TmuxService
    {
        private readonly string _frontClientId;
        private readonly string _frontClientTtyMarkerPath;

        public TmuxService()
        {
            _frontClientId = Guid.NewGuid().ToString("N")[..8];
            _frontClientTtyMarkerPath = "/tmp/halocreek/front-client-" + _frontClientId + ".tty";
        }

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

        public TerminalCommandSpec GetFrontClientStartupCommand(string initialIdentifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(initialIdentifier);

            var script = string.Join(
                "\n",
                "#!/usr/bin/env bash",
                "set -e",
                "mkdir -p " + QuoteBashArgument("/tmp/halocreek"),
                "tty > " + QuoteBashArgument(_frontClientTtyMarkerPath),
                "exec tmux attach-session -t " + QuoteBashArgument(initialIdentifier),
                string.Empty);

            return new TerminalWslScriptCommandSpec(
                "/",
                "halocreek-front-client-" + _frontClientId + "-" + initialIdentifier,
                script);
        }

        public void SwitchFrontClient(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
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

        private static string QuoteBashArgument(string value)
        {
            if (value.Length == 0)
            {
                return "''";
            }

            return "'" + value.Replace("'", "'\\''") + "'";
        }
    }
}
