using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HaloCreek.Infrastructure;
using HaloCreek.Logging;

namespace HaloCreek.Services
{
    public sealed class TmuxService : IDisposable
    {
        private static readonly TimeSpan CodexHistoryMatchTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan CodexHistoryMatchPollInterval = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan SendKeysEnterDelay = TimeSpan.FromMilliseconds(100);
        private const string LogCategory = "Tmux";
        private const string PsmuxExecutableName = "psmux";
        private const string PsmuxAttachScriptRelativePath = @"scripts\psmux_attach.bat";
        private const int SendKeysChunkLength = 4000;

        private readonly object _sessionOperationTasksLock = new();
        private readonly object _ownedSessionsLock = new();
        private readonly Dictionary<string, Task> _sessionOperationTasks = new(StringComparer.Ordinal);
        private readonly HashSet<string> _ownedSessionIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _sessionWorkspacePaths = new(StringComparer.Ordinal);
        private bool _isDisposed;

        public TmuxService(AppCommonRuntime appCommonRuntime)
        {
            ArgumentNullException.ThrowIfNull(appCommonRuntime);
        }

        public Task<TmuxLaunchResult> LaunchAsync(TmuxLaunchRequest request, out string identifier)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspacePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutableName);
            ArgumentNullException.ThrowIfNull(request.Arguments);

            var windowsWorkspacePath = PlatformInfrastructure.ConvertPathToWindows(request.WorkspacePath);
            var sessionIdentifier = CreateSessionIdentifier(windowsWorkspacePath);
            identifier = sessionIdentifier;
            var arguments = new[]
                {
                    "new-session",
                    "-d",
                    "-s",
                    sessionIdentifier,
                    "-c",
                    windowsWorkspacePath,
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
                RunMuxCommand(arguments, "launch psmux session");
                lock (_ownedSessionsLock)
                {
                    _ownedSessionIds.Add(sessionIdentifier);
                    _sessionWorkspacePaths[sessionIdentifier] = windowsWorkspacePath;
                }

                TryRunMuxCommand(new[] { "set-option", "-t", sessionIdentifier, "mouse", "on" }, out _);
                SetSessionMetadata(sessionIdentifier, windowsWorkspacePath, request.Title);

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
                TryRunMuxCommand(new[] { "kill-session", "-t", identifier }, out _);
                lock (_ownedSessionsLock)
                {
                    _ownedSessionIds.Remove(identifier);
                    _sessionWorkspacePaths.Remove(identifier);
                }
            });
        }

        // TODO Open CLI now always launches a wt tab; FrontClient naming should be revisited separately.
        public TerminalCommandSpec GetFrontClientStartupCommand(string initialIdentifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(initialIdentifier);

            string workspacePath;
            lock (_ownedSessionsLock)
            {
                if (!_sessionWorkspacePaths.TryGetValue(initialIdentifier, out workspacePath!))
                {
                    throw new InvalidOperationException("Session workspace is unavailable.");
                }
            }

            var attachScriptPath = Path.Combine(
                AppContext.BaseDirectory,
                PsmuxAttachScriptRelativePath);
            if (!File.Exists(attachScriptPath))
            {
                throw new FileNotFoundException(
                    "psmux attach script is missing from app output.",
                    attachScriptPath);
            }

            return new TerminalWindowsCommandSpec(
                workspacePath,
                "cmd.exe",
                new[] { "/d", "/c", "call", attachScriptPath, "-t", initialIdentifier });
        }

        public Task SendMessageToSessionAsync(
            string identifier,
            string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
            ArgumentNullException.ThrowIfNull(message);

            if (string.IsNullOrWhiteSpace(message))
            {
                return Task.CompletedTask;
            }

            return QueueSessionOperation(identifier, () =>
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
                TryRunMuxCommand(new[] { "kill-session", "-t", ownedSessionId }, out _);
            }
        }

        private void SetSessionMetadata(
            string identifier,
            string workspacePath,
            string title)
        {
            TrySetSessionOption(identifier, "@halocreek.workspace", workspacePath);
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
            TryRunMuxCommand(
                new[] { "set-option", "-q", "-t", identifier, optionName, optionValue },
                out _);
        }

        private void SendMessageToSessionCore(
            string identifier,
            string message)
        {
            var targetPane = identifier + ":0.0";
            var normalizedMsg = message.Replace("\r\n", "\r").Replace('\n', '\r');
            /*
             psmux literal send truncates at LF in this path; 
             CR reaches Codex as Enter and is handled by Codex paste-burst as multiline paste.
            */
            for (var offset = 0; offset < message.Length; offset += SendKeysChunkLength)
            {
                var chunkLength = Math.Min(SendKeysChunkLength, message.Length - offset);
                RunMuxCommand(
                    new[] { "send-keys", "-l", "-t", targetPane, normalizedMsg },
                    "send literal keys to psmux session");
            }

            Thread.Sleep(SendKeysEnterDelay); // 立刻enter会被吞 可能是codex cli自己的死区

            RunMuxCommand(
                new[] { "send-keys", "-t", targetPane, "Enter" },
                "send enter key to psmux session");

            Log.Info(
                LogCategory,
                $"Sent message to psmux session with send-keys. Session={identifier}, Length={message.Length}");
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
                // Mux cleanup is best effort; callers continue after observing task failures.
            }
        }

        private void RunMuxCommand(
            IReadOnlyList<string> arguments,
            string operationName)
        {
            if (TryRunMuxCommand(arguments, out var output))
            {
                return;
            }

            var message = NormalizeProcessOutput(output);
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "psmux command failed.";
            }

            throw new InvalidOperationException(
                "Failed to " + operationName + ": " + message);
        }

        private static bool TryRunMuxCommand(
            IReadOnlyList<string> arguments,
            out string output)
        {
            return PlatformInfrastructure.TryRunWindowsCommand(PsmuxExecutableName, arguments, out output);
        }

        private static string CreateSessionIdentifier(string workspacePath)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(workspacePath.Trim()));
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
