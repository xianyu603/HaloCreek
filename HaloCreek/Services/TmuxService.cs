using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using HaloCreek.Infrastructure;
using HaloCreek.Models;

namespace HaloCreek.Services
{
    public sealed class TmuxService : IDisposable
    {
        private const string HaloCreekTempDirectory = "/tmp/halocreek";
        private static readonly TimeSpan WatchPollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan BackgroundIdleThreshold = TimeSpan.FromSeconds(10);

        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly string _frontClientId;
        private readonly string _frontClientTtyMarkerPath;
        private readonly string _keeperSessionId;
        private readonly object _watchLock = new();
        private readonly Dictionary<string, WatchedSessionState> _watchedSessions = new(StringComparer.Ordinal);
        private readonly Timer _watchTimer;
        private string? _frontSessionId;
        private bool _isWatchProbeRunning;
        private bool _isDisposed;

        public TmuxService(PlatformInfrastructure platformInfrastructure)
        {
            _platformInfrastructure = platformInfrastructure
                ?? throw new ArgumentNullException(nameof(platformInfrastructure));
            _frontClientId = Guid.NewGuid().ToString("N")[..8];
            _keeperSessionId = "halocreek-keeper-" + _frontClientId;
            _frontClientTtyMarkerPath = HaloCreekTempDirectory
                + "/front-client-"
                + _frontClientId
                + ".tty";
            _watchTimer = new Timer(
                OnWatchTimerTick,
                state: null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            EnsureKeeperSession();
        }

        public event EventHandler<TmuxSessionStateChangedEventArgs>? StateChanged;

        public string Launch(TmuxLaunchRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspacePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutableName);
            ArgumentNullException.ThrowIfNull(request.Arguments);

            var wslWorkspacePath = _platformInfrastructure.ConvertPathToWsl(request.WorkspacePath);
            var identifier = CreateSessionIdentifier(wslWorkspacePath);
            var arguments = new[]
                {
                    "new-session",
                    "-d",
                    "-s",
                    identifier,
                    "-c",
                    wslWorkspacePath,
                    "--",
                    request.ExecutableName
                }
                .Concat(request.Arguments)
                .ToArray();

            RunTmuxCommand(arguments, "launch tmux session");

            TryRunTmuxCommand(new[] { "set-option", "-t", identifier, "mouse", "on" }, out _);
            SetSessionMetadata(identifier, wslWorkspacePath, request.Title);
            return identifier;
        }

        public void Exit(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            if (string.Equals(_frontSessionId, identifier, StringComparison.Ordinal))
            {
                SwitchFrontClientCore(_keeperSessionId);
                _frontSessionId = null;
            }

            TryRunTmuxCommand(new[] { "kill-session", "-t", identifier }, out _);
        }

        public TerminalCommandSpec GetFrontClientStartupCommand(string initialIdentifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(initialIdentifier);

            var script = string.Join(
                "\n",
                "#!/usr/bin/env bash",
                "set -e",
                "mkdir -p " + _platformInfrastructure.QuoteWslShellArgument(HaloCreekTempDirectory),
                "tty > " + _platformInfrastructure.QuoteWslShellArgument(_frontClientTtyMarkerPath),
                "exec " + _platformInfrastructure.BuildWslShellCommand(
                    "tmux",
                    new[] { "attach-session", "-t", initialIdentifier }),
                string.Empty);

            return new TerminalWslScriptCommandSpec(
                "/",
                "halocreek-front-client-" + _frontClientId + "-" + initialIdentifier,
                script);
        }

        public void SwitchFrontClient(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            SwitchFrontClientCore(identifier);
            _frontSessionId = identifier;
        }

        public void Cleanup()
        {
            _frontSessionId = null;
            TryRunTmuxCommand(new[] { "kill-session", "-t", _keeperSessionId }, out _);
        }

        public void Dispose()
        {
            lock (_watchLock)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _watchedSessions.Clear();
                _watchTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            _watchTimer.Dispose();
        }

        private void SwitchFrontClientCore(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            if (!_platformInfrastructure.TryRunWslCommand(
                    "cat",
                    new[] { _frontClientTtyMarkerPath },
                    out var output))
            {
                return;
            }

            var tty = SplitProcessOutputLines(output).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tty))
            {
                return;
            }

            TryRunTmuxCommand(
                new[] { "switch-client", "-c", tty, "-t", identifier },
                out _);
        }

        public void StartWatching(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            lock (_watchLock)
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);

                _watchedSessions[identifier] = new WatchedSessionState();
                if (!_isWatchProbeRunning)
                {
                    _watchTimer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
                }
            }
        }

        public void StopWatching(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            lock (_watchLock)
            {
                if (_isDisposed)
                {
                    return;
                }

                _watchedSessions.Remove(identifier);
                if (_watchedSessions.Count == 0)
                {
                    _watchTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
            }
        }

        private void OnStateChanged(TmuxSessionStateChangedEventArgs args)
        {
            StateChanged?.Invoke(this, args);
        }

        private void OnWatchTimerTick(object? state)
        {
            string[] identifiers;
            lock (_watchLock)
            {
                if (_isDisposed || _isWatchProbeRunning || _watchedSessions.Count == 0)
                {
                    return;
                }

                _isWatchProbeRunning = true;
                identifiers = _watchedSessions.Keys.ToArray();
            }

            try
            {
                foreach (var identifier in identifiers)
                {
                    ProbeAndPublishWatchedState(identifier);
                }
            }
            finally
            {
                lock (_watchLock)
                {
                    _isWatchProbeRunning = false;
                    if (!_isDisposed && _watchedSessions.Count > 0)
                    {
                        _watchTimer.Change(WatchPollInterval, Timeout.InfiniteTimeSpan);
                    }
                }
            }
        }

        private void ProbeAndPublishWatchedState(string identifier)
        {
            var probeSucceeded = TryRunTmuxCommand(
                new[] { "capture-pane", "-p", "-t", identifier, "-S", "-200" },
                out var output);

            var now = DateTimeOffset.UtcNow;
            var snapshot = probeSucceeded
                ? NormalizeProcessOutput(output)
                : string.Empty;
            var newState = OngoingSessionState.Unknown;
            var shouldNotify = false;

            lock (_watchLock)
            {
                if (_isDisposed
                    || !_watchedSessions.TryGetValue(identifier, out var watchedSession))
                {
                    return;
                }

                if (probeSucceeded)
                {
                    if (watchedSession.PaneSnapshot is null
                        || !string.Equals(watchedSession.PaneSnapshot, snapshot, StringComparison.Ordinal))
                    {
                        watchedSession.PaneSnapshot = snapshot;
                        watchedSession.LastOutputChangedAt = now;
                        newState = OngoingSessionState.BackgroundRunning;
                    }
                    else
                    {
                        newState = now - watchedSession.LastOutputChangedAt <= BackgroundIdleThreshold
                            ? OngoingSessionState.BackgroundRunning
                            : OngoingSessionState.BackgroundIdle;
                    }
                }

                if (watchedSession.State != newState)
                {
                    watchedSession.State = newState;
                    shouldNotify = true;
                }
            }

            if (shouldNotify)
            {
                OnStateChanged(new TmuxSessionStateChangedEventArgs(identifier, newState));
            }
        }

        private sealed class WatchedSessionState
        {
            public OngoingSessionState State { get; set; } = OngoingSessionState.Unknown;

            public string? PaneSnapshot { get; set; }

            public DateTimeOffset LastOutputChangedAt { get; set; } = DateTimeOffset.UtcNow;
        }

        private void SetSessionMetadata(
            string identifier,
            string wslWorkspacePath,
            string title)
        {
            TrySetSessionOption(identifier, "@halocreek.workspace", wslWorkspacePath);
            TrySetSessionOption(identifier, "@halocreek.startedAt", DateTimeOffset.Now.ToString("O"));
            if (!string.IsNullOrWhiteSpace(title))
            {
                TrySetSessionOption(identifier, "@halocreek.title", title.Trim());
            }
        }

        private void TrySetSessionOption(
            string identifier,
            string optionName,
            string optionValue)
        {
            TryRunTmuxCommand(
                new[] { "set-option", "-q", "-t", identifier, optionName, optionValue },
                out _);
        }

        private void EnsureKeeperSession()
        {
            if (TryRunTmuxCommand(new[] { "has-session", "-t", _keeperSessionId }, out _))
            {
                return;
            }

            RunTmuxCommand(
                new[] { "new-session", "-d", "-s", _keeperSessionId, "-c", "/", "--", "bash" },
                "launch tmux keeper session");
        }

        private void RunTmuxCommand(
            IReadOnlyList<string> arguments,
            string operationName)
        {
            if (TryRunTmuxCommand(arguments, out var output))
            {
                return;
            }

            var message = NormalizeProcessOutput(output);
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "tmux command failed.";
            }

            throw new InvalidOperationException(
                "Failed to " + operationName + ": " + message);
        }

        private bool TryRunTmuxCommand(
            IReadOnlyList<string> arguments,
            out string output)
        {
            return _platformInfrastructure.TryRunWslCommand("tmux", arguments, out output);
        }

        private static string CreateSessionIdentifier(string wslWorkspacePath)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(wslWorkspacePath.Trim()));
            var workspaceHash = Convert.ToHexString(hash)[..12].ToLowerInvariant();
            var shortId = Guid.NewGuid().ToString("N")[..8];
            return "halocreek-" + workspaceHash + "-" + shortId;
        }

        private static IEnumerable<string> SplitProcessOutputLines(string output)
        {
            return NormalizeProcessOutput(output)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
        }

        private static string NormalizeProcessOutput(string output)
        {
            return output.Replace("\0", string.Empty).Trim();
        }
    }
}
