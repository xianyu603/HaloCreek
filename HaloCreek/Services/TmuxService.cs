using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HaloCreek.Infrastructure;

namespace HaloCreek.Services
{
    public sealed class TmuxService : IDisposable
    {
        private static readonly TimeSpan CodexHistoryMatchTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CodexHistoryMatchPollInterval = TimeSpan.FromMilliseconds(250);

        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly string _frontClientId;
        private readonly object _sessionOperationTasksLock = new();
        private readonly object _ownedSessionsLock = new();
        private readonly Dictionary<string, Task> _sessionOperationTasks = new(StringComparer.Ordinal);
        private readonly HashSet<string> _ownedSessionIds = new(StringComparer.Ordinal);
        private bool _isDisposed;

        public TmuxService(AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);

            _platformInfrastructure = appCommonRuntime.PlatformInfrastructure;
            _frontClientId = Guid.NewGuid().ToString("N")[..8];
        }

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

        // TODO Open CLI now always launches a wt tab; FrontClient naming should be revisited separately.
        public TerminalCommandSpec GetFrontClientStartupCommand(string initialIdentifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(initialIdentifier);

            var script = string.Join(
                "\n",
                "#!/usr/bin/env bash",
                "set -e",
                "exec " + _platformInfrastructure.BuildWslShellCommand(
                    "tmux",
                    new[] { "attach-session", "-t", initialIdentifier }),
                string.Empty);

            return new TerminalWslScriptCommandSpec(
                "/",
                "halocreek-front-client-" + _frontClientId + "-" + initialIdentifier,
                script);
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
