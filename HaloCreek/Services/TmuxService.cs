using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HaloCreek.Infrastructure;
using HaloCreek.Models;
using HaloCreek.Services.WorkspaceSnapshots;

namespace HaloCreek.Services
{
    public sealed class TmuxService : IDisposable
    {
        private const string HaloCreekTempDirectory = "/tmp/halocreek";
        private static readonly TimeSpan CodexHistoryMatchTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CodexHistoryMatchPollInterval = TimeSpan.FromMilliseconds(250);

        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly string _frontClientId;
        private readonly string _frontClientTtyMarkerPath;
        private readonly object _sessionOperationTasksLock = new();
        private readonly object _ownedSessionsLock = new();
        private readonly Dictionary<string, TmuxHeartbeatWatch> _heartbeatWatchesById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Task> _sessionOperationTasks = new(StringComparer.Ordinal);
        private readonly HashSet<string> _ownedSessionIds = new(StringComparer.Ordinal);
        private readonly FrontClientSwitcher _frontClientSwitcher;
        private bool _isDisposed;

        public TmuxService(AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _platformInfrastructure = appCommonRuntime.PlatformInfrastructure;
            _frontClientId = Guid.NewGuid().ToString("N")[..8];
            _frontClientTtyMarkerPath = HaloCreekTempDirectory
                + "/front-client-"
                + _frontClientId
                + ".tty";
            _frontClientSwitcher = new FrontClientSwitcher(
                _platformInfrastructure,
                _frontClientTtyMarkerPath);
        }

        public event EventHandler<TmuxSessionStateChangedEventArgs>? StateChanged;

        public Task<TmuxLaunchResult> LaunchAsync(TmuxLaunchRequest request, out string identifier)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspacePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutableName);
            ArgumentNullException.ThrowIfNull(request.Arguments);

            var wslWorkspacePath = _platformInfrastructure.ConvertPathToWsl(request.WorkspacePath);
            var sessionIdentifier = CreateSessionIdentifier(wslWorkspacePath);
            identifier = sessionIdentifier;
            var arguments = new[]
                {
                    "new-session",
                    "-d",
                    "-s",
                    sessionIdentifier,
                    "-c",
                    wslWorkspacePath,
                    "--",
                    request.ExecutableName
                }
                .Concat(request.Arguments)
                .ToArray();

            return QueueSessionOperation(sessionIdentifier, () =>
            {
                var historySnapshot = string.IsNullOrWhiteSpace(request.KnownCodexSessionId)
                    ? CodexSessionFileLocator.CaptureHistorySnapshot()
                    : null;
                RunTmuxCommand(arguments, "launch tmux session");
                lock (_ownedSessionsLock)
                {
                    _ownedSessionIds.Add(sessionIdentifier);
                }

                StartHeartbeatPipe(sessionIdentifier);
                TryRunTmuxCommand(new[] { "set-option", "-t", sessionIdentifier, "mouse", "on" }, out _);
                SetSessionMetadata(sessionIdentifier, wslWorkspacePath, request.Title);

                var codexSessionId = !string.IsNullOrWhiteSpace(request.KnownCodexSessionId)
                    ? request.KnownCodexSessionId.Trim()
                    : WaitForCodexSessionId(historySnapshot!, request.HistoryPromptText);
                return new TmuxLaunchResult(sessionIdentifier, codexSessionId);
            });
        }

        public void Exit(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
            QueueSessionOperation(identifier, () =>
            {
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

        public bool HasFrontClient()
        {
            return _frontClientSwitcher.HasClient();
        }

        public void MarkFrontClientAttachedToSession(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
            _frontClientSwitcher.MarkAttachedToSession(identifier);
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
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            DisposeHeartbeatWatches();
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
        }

        public void StartWatching(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(TmuxService));
            }

            if (_heartbeatWatchesById.TryGetValue(identifier, out var existingWatch))
            {
                existingWatch.Store.RequestRefresh(SnapshotRefreshReason.Manual);
                return;
            }

            var store = WorkspaceSnapshotStore.Create<TmuxHeartbeatSnapshot>(identifier);
            var watch = new TmuxHeartbeatWatch(
                identifier,
                store,
                (_, _) => OnHeartbeatSnapshotChanged(identifier, store.Current));
            store.Changed += watch.ChangedHandler;
            _heartbeatWatchesById.Add(identifier, watch);
            OnHeartbeatSnapshotChanged(identifier, store.Current);// empty store正常都是异步读取的
        }

        public void StopWatching(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

            if (_isDisposed)
            {
                return;
            }

            RemoveHeartbeatWatch(identifier);
        }

        private void OnStateChanged(TmuxSessionStateChangedEventArgs args)
        {
            StateChanged?.Invoke(this, args);
        }

        private void OnHeartbeatSnapshotChanged(
            string identifier,
            TmuxHeartbeatSnapshot snapshot)
        {
            if (_isDisposed)
            {
                return;
            }

            var sessionState = snapshot.State switch
            {
                TmuxHeartbeatState.Active => OngoingSessionState.BackgroundRunning,
                TmuxHeartbeatState.Idle => OngoingSessionState.BackgroundIdle,
                _ => throw new InvalidOperationException("Unknown tmux heartbeat state: " + snapshot.State),
            };

            OnStateChanged(new TmuxSessionStateChangedEventArgs(identifier, sessionState));
        }

        private void RemoveHeartbeatWatch(string identifier)
        {
            if (!_heartbeatWatchesById.Remove(identifier, out var watch))
            {
                return;
            }

            watch.Dispose();
        }

        private void DisposeHeartbeatWatches()
        {
            foreach (var watch in _heartbeatWatchesById.Values)
            {
                watch.Dispose();
            }

            _heartbeatWatchesById.Clear();
        }

        private sealed class TmuxHeartbeatWatch : IDisposable
        {
            public TmuxHeartbeatWatch(
                string identifier,
                WorkspaceSnapshotStore<TmuxHeartbeatSnapshot> store,
                EventHandler changedHandler)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

                Store = store ?? throw new ArgumentNullException(nameof(store));
                ChangedHandler = changedHandler ?? throw new ArgumentNullException(nameof(changedHandler));
            }

            public WorkspaceSnapshotStore<TmuxHeartbeatSnapshot> Store { get; }

            public EventHandler ChangedHandler { get; }

            public void Dispose()
            {
                Store.Changed -= ChangedHandler;
                Store.Dispose();
            }
        }

        private sealed class FrontClientSwitcher
        {
            private readonly PlatformInfrastructure _platformInfrastructure;
            private readonly string _frontClientTtyMarkerPath;
            private readonly object _lock = new();
            private string? _frontSessionId;

            public FrontClientSwitcher(
                PlatformInfrastructure platformInfrastructure,
                string frontClientTtyMarkerPath)
            {
                _platformInfrastructure = platformInfrastructure
                    ?? throw new ArgumentNullException(nameof(platformInfrastructure));
                ArgumentException.ThrowIfNullOrWhiteSpace(frontClientTtyMarkerPath);

                _frontClientTtyMarkerPath = frontClientTtyMarkerPath;
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

            public void Clear()
            {
                lock (_lock)
                {
                    _frontSessionId = null;
                }
            }

            public void MarkAttachedToSession(string identifier)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

                lock (_lock)
                {
                    _frontSessionId = identifier;
                }
            }

            private void SwitchClientCore(string identifier)
            {
                if (!TryGetFrontClientTty(out var tty))
                {
                    throw new InvalidOperationException("Front tmux client is not available.");
                }

                if (!_platformInfrastructure.TryRunWslCommand(
                        "tmux",
                        new[] { "switch-client", "-c", tty, "-t", identifier },
                        out var output))
                {
                    var message = NormalizeProcessOutput(output);
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        message = "tmux switch-client failed.";
                    }

                    throw new InvalidOperationException(
                        "Failed to switch front tmux client: " + message);
                }
            }

            public bool HasClient()
            {
                lock (_lock)
                {
                    if (!TryGetFrontClientTty(out var tty))
                    {
                        return false;
                    }

                    if (!_platformInfrastructure.TryRunWslCommand(
                            "tmux",
                            new[] { "list-clients", "-F", "#{client_tty}" },
                            out var output))
                    {
                        return false;
                    }

                    return SplitProcessOutputLines(output)
                        .Any(clientTty => string.Equals(clientTty, tty, StringComparison.Ordinal));
                }
            }

            private bool TryGetFrontClientTty(out string tty)
            {
                if (!_platformInfrastructure.TryRunWslCommand(
                        "cat",
                        new[] { _frontClientTtyMarkerPath },
                        out var output))
                {
                    tty = string.Empty;
                    return false;
                }

                tty = SplitProcessOutputLines(output).FirstOrDefault() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(tty);
            }
        }

        private void StartHeartbeatPipe(string identifier)
        {
            var heartbeatPath = TmuxHeartbeatSnapshot.GetHeartbeatPath(identifier);
            _platformInfrastructure.TryRunWslCommand(
                "mkdir",
                new[] { "-p", TmuxHeartbeatSnapshot.HeartbeatDirectory },
                out _);
            _platformInfrastructure.TryRunWslCommand(
                "touch",
                new[] { heartbeatPath },
                out _);

            // The helper consumes pane output without storing it. It treats Codex's
            // repeated OSC title updates as the background activity signal, so tmux
            // redraws and ordinary pane output do not move an idle session back to
            // running after it is switched to the front client.
            var helperScript = string.Join(
                " ",
                "heartbeat=$1;",
                "last_touch=0;",
                "esc=$(printf '\\033');",
                "previous=;",
                "while IFS= read -r -n 1 char; do",
                "if [ \"$previous\" = \"$esc\" ] && [ \"$char\" = \"]\" ]; then",
                "now=${EPOCHSECONDS:-$(date +%s)};", // 输出以sec为单位的整数时间 以实现最多1s touch一次
                "if [ \"$now\" != \"$last_touch\" ]; then",
                ": > \"$heartbeat\";",
                "last_touch=$now;",
                "fi;",
                "fi;",
                "previous=$char;",
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
            return QueueSessionOperation<object?>(identifier, () =>
            {
                operation();
                return null;
            });
        }

        private Task<TResult> QueueSessionOperation<TResult>(string identifier, Func<TResult> operation)
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

                    return operation();
                });

                _sessionOperationTasks[identifier] = task;
                _ = task.ContinueWith(
                    completedTask => RemoveCompletedSessionOperation(identifier, completedTask),
                    TaskScheduler.Default);
                return task;
            }
        }

        private static string WaitForCodexSessionId(
            CodexHistorySnapshot historySnapshot,
            string? promptText)
        {
            if (string.IsNullOrWhiteSpace(promptText))
            {
                throw new InvalidOperationException(
                    "Codex history prompt text is required to match the launched session.");
            }

            var deadline = DateTimeOffset.UtcNow + CodexHistoryMatchTimeout;
            while (true)
            {
                var entry = CodexSessionFileLocator.FindNewHistoryEntry(
                    historySnapshot,
                    promptText);
                if (entry is not null)
                {
                    return entry.SessionId;
                }

                if (DateTimeOffset.UtcNow >= deadline)
                {
                    break;
                }

                Thread.Sleep(CodexHistoryMatchPollInterval);
            }

            throw new InvalidOperationException(
                "Timed out waiting for Codex history to record the launched session.");
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
