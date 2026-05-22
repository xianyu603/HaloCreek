using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HaloCreek.Infrastructure;

namespace HaloCreek.Services
{
    public sealed class TmuxService
    {
        private const string HaloCreekTempDirectory = "/tmp/halocreek";

        private readonly PlatformInfrastructure _platformInfrastructure;
        private readonly string _frontClientId;
        private readonly string _frontClientTtyMarkerPath;

        public TmuxService(PlatformInfrastructure platformInfrastructure)
        {
            _platformInfrastructure = platformInfrastructure
                ?? throw new ArgumentNullException(nameof(platformInfrastructure));
            _frontClientId = Guid.NewGuid().ToString("N")[..8];
            _frontClientTtyMarkerPath = HaloCreekTempDirectory
                + "/front-client-"
                + _frontClientId
                + ".tty";
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
        }

        public void StopWatching(string identifier)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        }

        private void OnStateChanged(TmuxSessionStateChangedEventArgs args)
        {
            StateChanged?.Invoke(this, args);
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
