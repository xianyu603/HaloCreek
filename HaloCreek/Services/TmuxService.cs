using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HaloCreek.Infrastructure;
using HaloCreek.Models;


/*
 * 用显示状态机维护状态 避免boolean组合状态的问题
| Trigger | Current State | Action | Next State |
| --- | --- | --- | --- |
| `StartWatching` | `Idle` | Add session heartbeat path, schedule immediate timer | `Scheduled` |
| `StartWatching` | `Scheduled` | Add or reset session heartbeat path, keep timer scheduled | `Scheduled` |
| `StartWatching` | `Probing` | Add or reset session heartbeat path, let next probe cycle include it | `Probing` |
| `StartWatching` | `Disposed` | Throw `ObjectDisposedException` | `Disposed` |
| `StopWatching` | `Idle` | No-op after remove attempt | `Idle` |
| `StopWatching` | `Scheduled` with sessions left | Remove session, keep timer scheduled | `Scheduled` |
| `StopWatching` | `Scheduled` with no sessions left | Remove session, stop timer | `Idle` |
| `StopWatching` | `Probing` | Remove session; in-flight result for it will be ignored | `Probing` |
| `StopWatching` | `Disposed` | No-op | `Disposed` |
| Timer tick | `Scheduled` | Copy identifiers, enter probe cycle | `Probing` |
| Timer tick | `Idle` / `Probing` / `Disposed` | Ignore | Unchanged |
| Probe finish | `Probing` with sessions left | Schedule next timer | `Scheduled` |
| Probe finish | `Probing` with no sessions left | Keep timer stopped | `Idle` |
| Probe finish | `Disposed` | Keep timer stopped | `Disposed` |
| `Dispose` | Any state | Clear sessions, stop timer, wait pending session operation tasks, best effort kill keeper session | `Disposed` |
*/

namespace HaloCreek.Services
{
    public sealed class TmuxService : IDisposable
    {
        private const string HaloCreekTempDirectory = "/tmp/halocreek";
        private const string HeartbeatDirectory = HaloCreekTempDirectory + "/heartbeats";
        private static readonly TimeSpan WatchPollInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan BackgroundIdleThreshold = TimeSpan.FromSeconds(4);

        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly string _frontClientId;
        private readonly string _frontClientTtyMarkerPath;
        private readonly string _keeperSessionId;
        private readonly object _watchStateLock = new();
        private readonly object _sessionOperationTasksLock = new();
        private readonly object _ownedSessionsLock = new();
        private readonly Dictionary<string, WatchedSessionState> _watchedSessions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Task> _sessionOperationTasks = new(StringComparer.Ordinal);
        private readonly HashSet<string> _ownedSessionIds = new(StringComparer.Ordinal);
        private readonly Timer _watchTimer;
        private readonly FrontClientSwitcher _frontClientSwitcher;
        private WatchState _watchState;

        public TmuxService(AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _platformInfrastructure = appCommonRuntime.PlatformInfrastructure;
            _frontClientId = Guid.NewGuid().ToString("N")[..8];
            _keeperSessionId = "halocreek-keeper-" + _frontClientId;
            _frontClientTtyMarkerPath = HaloCreekTempDirectory
                + "/front-client-"
                + _frontClientId
                + ".tty";
            _frontClientSwitcher = new FrontClientSwitcher(
                _platformInfrastructure,
                _frontClientTtyMarkerPath,
                _keeperSessionId);
            _watchTimer = new Timer(
                OnWatchTimerTick,
                state: null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            EnsureKeeperSession();
        }

        public event EventHandler<TmuxSessionStateChangedEventArgs>? StateChanged;

        public async Task<string> LaunchAsync(TmuxLaunchRequest request)
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

            await QueueSessionOperation(identifier, () =>
            {
                RunTmuxCommand(arguments, "launch tmux session");
                lock (_ownedSessionsLock)
                {
                    _ownedSessionIds.Add(identifier);
                }

                StartHeartbeatPipe(identifier);
                TryRunTmuxCommand(new[] { "set-option", "-t", identifier, "mouse", "on" }, out _);
                SetSessionMetadata(identifier, wslWorkspacePath, request.Title);
            });

            return identifier;
        }

        public void Exit(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
            QueueSessionOperation(identifier, () =>
            {
                _frontClientSwitcher.SwitchOutIfFront(identifier);
                TryRunTmuxCommand(new[] { "kill-session", "-t", identifier }, out _);
                lock (_ownedSessionsLock)
                {
                    _ownedSessionIds.Remove(identifier);
                }
            });
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
            _frontClientSwitcher.SwitchIn(identifier);
        }

        public void SendMessageToSession(
            string identifier,
            string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
            ArgumentNullException.ThrowIfNull(message);

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            QueueSessionOperation(identifier, () =>
                SendMessageToSessionCore(identifier, message));
        }

        public void Dispose()
        {
            lock (_watchStateLock)
            {
                if (_watchState == WatchState.Disposed)
                {
                    return;
                }

                _watchState = WatchState.Disposed;
                _watchedSessions.Clear();
                _watchTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            _watchTimer.Dispose();
            WaitForSessionOperationsToComplete();
            string[] ownedSessionIds;
            lock (_ownedSessionsLock)
            {
                ownedSessionIds = _ownedSessionIds.ToArray();
                _ownedSessionIds.Clear();
            }

            foreach (var ownedSessionId in ownedSessionIds)
            {
                TryRunTmuxCommand(new[] { "kill-session", "-t", ownedSessionId }, out _);
            }

            _frontClientSwitcher.Clear();
            TryRunTmuxCommand(new[] { "kill-session", "-t", _keeperSessionId }, out _);
        }

        public void StartWatching(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            lock (_watchStateLock)
            {
                if (_watchState == WatchState.Disposed)
                {
                    throw new ObjectDisposedException(nameof(TmuxService));
                }

                _watchedSessions[identifier] = new WatchedSessionState(
                    GetHeartbeatPath(identifier));
                if (_watchState == WatchState.Idle)
                {
                    _watchState = WatchState.Scheduled;
                    _watchTimer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
                }
            }
        }

        public void StopWatching(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            lock (_watchStateLock)
            {
                if (_watchState == WatchState.Disposed)
                {
                    return;
                }

                _watchedSessions.Remove(identifier);
                if (_watchedSessions.Count == 0
                    && _watchState == WatchState.Scheduled)
                {
                    _watchState = WatchState.Idle;
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
            lock (_watchStateLock)
            {
                if (_watchState != WatchState.Scheduled)
                {
                    return;
                }

                _watchState = WatchState.Probing;
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
                lock (_watchStateLock)
                {
                    if (_watchState == WatchState.Probing
                        && _watchedSessions.Count > 0)
                    {
                        _watchState = WatchState.Scheduled;
                        _watchTimer.Change(WatchPollInterval, Timeout.InfiniteTimeSpan);
                    }
                    else if (_watchState == WatchState.Probing)
                    {
                        _watchState = WatchState.Idle;
                    }
                }
            }
        }

        private void ProbeAndPublishWatchedState(string identifier)
        {
            string heartbeatPath;
            lock (_watchStateLock)
            {
                if (_watchState == WatchState.Disposed
                    || !_watchedSessions.TryGetValue(identifier, out var watchedSession))
                {
                    return;
                }

                heartbeatPath = watchedSession.HeartbeatPath;
            }

            var now = DateTimeOffset.UtcNow;
            var heartbeatExists = _platformInfrastructure.TryGetWslFileLastWriteTimeUtc(
                heartbeatPath,
                out var heartbeatLastWriteTimeUtc);
            var newState = heartbeatExists
                ? now - heartbeatLastWriteTimeUtc < BackgroundIdleThreshold
                    ? OngoingSessionState.BackgroundRunning
                    : OngoingSessionState.BackgroundIdle
                : OngoingSessionState.Unknown;
            var shouldNotify = false;

            lock (_watchStateLock)
            {
                if (_watchState == WatchState.Disposed
                    || !_watchedSessions.TryGetValue(identifier, out var watchedSession))
                {
                    return;
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

        private enum WatchState
        {
            Idle,
            Scheduled,
            Probing,
            Disposed
        }

        private sealed class WatchedSessionState
        {
            public WatchedSessionState(string heartbeatPath)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(heartbeatPath);

                HeartbeatPath = heartbeatPath;
            }

            public OngoingSessionState State { get; set; } = OngoingSessionState.Unknown;

            public string HeartbeatPath { get; }
        }

        private sealed class FrontClientSwitcher
        {
            private readonly PlatformInfrastructure _platformInfrastructure;
            private readonly string _frontClientTtyMarkerPath;
            private readonly string _keeperSessionId;
            private readonly object _lock = new();
            private string? _frontSessionId;

            public FrontClientSwitcher(
                PlatformInfrastructure platformInfrastructure,
                string frontClientTtyMarkerPath,
                string keeperSessionId)
            {
                _platformInfrastructure = platformInfrastructure
                    ?? throw new ArgumentNullException(nameof(platformInfrastructure));
                ArgumentException.ThrowIfNullOrWhiteSpace(frontClientTtyMarkerPath);
                ArgumentException.ThrowIfNullOrWhiteSpace(keeperSessionId);

                _frontClientTtyMarkerPath = frontClientTtyMarkerPath;
                _keeperSessionId = keeperSessionId;
            }

            public void SwitchIn(string identifier)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

                lock (_lock)
                {
                    SwitchClientCore(identifier);
                    _frontSessionId = identifier;
                }
            }

            public void SwitchOutIfFront(string identifier)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

                lock (_lock)
                {
                    if (!string.Equals(_frontSessionId, identifier, StringComparison.Ordinal))
                    {
                        return;
                    }

                    SwitchClientCore(_keeperSessionId);
                    _frontSessionId = null;
                }
            }

            public void Clear()
            {
                lock (_lock)
                {
                    _frontSessionId = null;
                }
            }

            public bool TryGetFrontSessionId(out string frontSessionId)
            {
                lock (_lock)
                {
                    if (string.IsNullOrWhiteSpace(_frontSessionId))
                    {
                        frontSessionId = string.Empty;
                        return false;
                    }

                    frontSessionId = _frontSessionId;
                    return true;
                }
            }

            private void SwitchClientCore(string identifier)
            {
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

                _platformInfrastructure.TryRunWslCommand(
                    "tmux",
                    new[] { "switch-client", "-c", tty, "-t", identifier },
                    out _);
            }
        }

        private void StartHeartbeatPipe(string identifier)
        {
            var heartbeatPath = GetHeartbeatPath(identifier);
            _platformInfrastructure.TryRunWslCommand(
                "mkdir",
                new[] { "-p", HeartbeatDirectory },
                out _);
            _platformInfrastructure.TryRunWslCommand(
                "touch",
                new[] { heartbeatPath },
                out _);

            // The helper consumes pane output without storing it. It refreshes the
            // heartbeat at most once per second, while sparse output still refreshes
            // on the next non-empty read even if the pane output has no newline.
            var helperScript = string.Join(
                " ",
                "heartbeat=$1;",
                "last_touch=0;",
                "while :; do",
                "chunk=;",
                "IFS= read -r -n 4096 -t 1 chunk;", // 一次读4096 如果没那么多可读1s超时一次
                "status=$?;",
                "if [ -n \"$chunk\" ]; then",
                "now=${EPOCHSECONDS:-$(date +%s)};", // 输出以sec为单位的整数时间 以实现最多1s touch一次
                "if [ \"$now\" != \"$last_touch\" ]; then",
                ": > \"$heartbeat\";",
                "last_touch=$now;",
                "fi;",
                "fi;",
                "if [ \"$status\" -eq 1 ] && [ -z \"$chunk\" ]; then",
                "break;",
                "fi;",
                "done");

            var helperCommand = _platformInfrastructure.BuildWslShellCommand(
                "bash",
                new[] { "-c", helperScript, "--", heartbeatPath });

            // MVP boundary: HaloCreek-created sessions are single-window/single-pane.
            // If the user manually changes panes or kills tmux outside HaloCreek, the
            // heartbeat may age into idle instead of representing that external change.
            RunTmuxCommand(
                new[] { "pipe-pane", "-o", "-t", identifier + ":0.0", helperCommand },
                "start heartbeat pipe");
        }

        private static string GetHeartbeatPath(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            return HeartbeatDirectory + "/" + identifier + ".heartbeat";
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

        private void SendMessageToSessionCore(
            string identifier,
            string message)
        {
            var targetPane = identifier + ":0.0";
            var bufferName = "halocreek-send-"
                + _frontClientId
                + "-"
                + Guid.NewGuid().ToString("N")[..8];

            if (!TryRunTmuxCommand(
                    new[] { "set-buffer", "-b", bufferName, message },
                    out _))
            {
                return;
            }

            if (!TryRunTmuxCommand(
                    new[] { "paste-buffer", "-d", "-b", bufferName, "-t", targetPane },
                    out _))
            {
                return;
            }

            if (!TryRunTmuxCommand(
                    new[] { "send-keys", "-t", targetPane, "Enter" },
                    out _))
            {
                return;
            }
        }

        private Task QueueSessionOperation(string identifier, Action operation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
            ArgumentNullException.ThrowIfNull(operation);

            lock (_sessionOperationTasksLock)
            {
                _sessionOperationTasks.TryGetValue(identifier, out var previousTask);
                var task = Task.Run(() =>
                {
                    if (previousTask is not null)
                    {
                        WaitForTaskToComplete(previousTask);
                    }

                    operation();
                });

                _sessionOperationTasks[identifier] = task;
                _ = task.ContinueWith(
                    completedTask => RemoveCompletedSessionOperation(identifier, completedTask),
                    TaskScheduler.Default);
                return task;
            }
        }

        private void RemoveCompletedSessionOperation(string identifier, Task completedTask)
        {
            lock (_sessionOperationTasksLock)
            {
                if (_sessionOperationTasks.TryGetValue(identifier, out var currentTask)
                    && ReferenceEquals(currentTask, completedTask))
                {
                    _sessionOperationTasks.Remove(identifier);
                }
            }
        }

        private void WaitForSessionOperationsToComplete()
        {
            Task[] tasks;
            lock (_sessionOperationTasksLock)
            {
                tasks = _sessionOperationTasks.Values.ToArray();
                _sessionOperationTasks.Clear();
            }

            foreach (var task in tasks)
            {
                WaitForTaskToComplete(task);
            }
        }

        private static void WaitForTaskToComplete(Task task)
        {
            try
            {
                task.Wait();
            }
            catch (AggregateException)
            {
                // Tmux cleanup is best effort; callers continue after observing task failures.
            }
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
