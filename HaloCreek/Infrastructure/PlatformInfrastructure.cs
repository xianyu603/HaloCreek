using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace HaloCreek.Infrastructure
{
    public sealed class PlatformInfrastructure
    {
        private const int WslCommandTimeoutMilliseconds = 3000;

        private readonly Window _owner;
        private IReadOnlyList<string>? _wslDistributionNames;

        public PlatformInfrastructure(Window owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public async Task<string?> SelectDirectoryAsync()
        {
            if (!_owner.StorageProvider.CanPickFolder)
            {
                throw new InvalidOperationException("Folder picker is not available.");
            }

            var folders = await _owner.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Select Workspace",
                    AllowMultiple = false,
                });

            if (folders.Count == 0)
            {
                return null;
            }

            try
            {
                var folder = folders[0];
                var localPath = folder.TryGetLocalPath();
                if (string.IsNullOrWhiteSpace(localPath))
                {
                    throw new InvalidOperationException("Selected folder does not expose a local path.");
                }

                return localPath;
            }
            finally
            {
                foreach (var folder in folders)
                {
                    folder.Dispose();
                }
            }
        }

        public async Task ShowErrorDialogAsync(string title, string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            ArgumentException.ThrowIfNullOrWhiteSpace(message);

            var dialog = new Window
            {
                Title = title,
                Width = 520,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
            };

            var closeButton = new Button
            {
                Content = "OK",
                MinWidth = 80,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            closeButton.Click += (_, _) => dialog.Close();

            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    closeButton,
                },
            };

            await dialog.ShowDialog(_owner);
        }

        /// <summary>
        /// Normalizes a directory path only when it points to an existing directory.
        /// Invalid, blank, or missing paths return false and do not expose a normalized path.
        /// If a caller needs normalize-only or allow-missing behavior, add an explicit API for that contract.
        /// </summary>
        public bool TryNormalizeExistingDirectoryPath(string? path, out string normalizedPath)
        {
            normalizedPath = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                var candidatePath = Path.GetFullPath(path.Trim());
                if (!Directory.Exists(candidatePath))
                {
                    return false;
                }

                normalizedPath = candidatePath;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
            {
                normalizedPath = string.Empty;
                return false;
            }
        }

        public static bool AreWorkspacePathsEquivalent(string? left, string? right)
        {
            var normalizedLeft = NormalizeWorkspacePathForComparison(left);
            var normalizedRight = NormalizeWorkspacePathForComparison(right);

            if (normalizedLeft is null || normalizedRight is null)
            {
                return false;
            }

            var comparison = (IsMountedWindowsDrivePath(normalizedLeft)
                || IsMountedWindowsDrivePath(normalizedRight))
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

            return string.Equals(normalizedLeft, normalizedRight, comparison);
        }

        public bool TryGetWslEnvironmentVariable(string name, out string value)
        {
            value = string.Empty;

            if (string.IsNullOrWhiteSpace(name)
                || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            {
                return false;
            }

            if (!OperatingSystem.IsWindows())
            {
                value = Environment.GetEnvironmentVariable(name) ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            }

            var command = $"printf '%s' \"${{{name}:-}}\"";
            if (!TryRunWslCommand(command, out var output))
            {
                return false;
            }

            value = NormalizeProcessOutput(output).Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        public IReadOnlyList<string> GetReadableWslPathCandidates(string wslPath)
        {
            if (string.IsNullOrWhiteSpace(wslPath))
            {
                return Array.Empty<string>();
            }

            var normalizedWslPath = wslPath.Trim().Replace('\\', '/');
            if (!normalizedWslPath.StartsWith("/", StringComparison.Ordinal)
                || !OperatingSystem.IsWindows())
            {
                return new[] { normalizedWslPath };
            }

            var windowsPathSuffix = normalizedWslPath.Replace('/', '\\');
            var candidates = new List<string>();
            foreach (var distributionName in GetWslDistributionNames())
            {
                candidates.Add(@"\\wsl.localhost\" + distributionName + windowsPathSuffix);
                candidates.Add(@"\\wsl$\" + distributionName + windowsPathSuffix);
            }

            return candidates;
        }

        public Process? LaunchWslTerminalCommand(
            string workspacePath,
            string executableName,
            IEnumerable<string> arguments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
            ArgumentNullException.ThrowIfNull(arguments);

            var wslWorkspacePath = ConvertToWslPath(workspacePath);
            var shellCommand = BuildShellCommand(executableName, arguments);

            return StartWindowsTerminal(wslWorkspacePath, shellCommand);
        }

        // TODO 这个类的命名需要整理
        public Process? LaunchTerminal(TerminalLaunchRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Command);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Command.WorkingDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Command.Executable);

            var wslWorkspacePath = ConvertToWslPath(request.Command.WorkingDirectory);
            var shellCommand = BuildShellCommand(
                request.Command.Executable,
                request.Command.Arguments);

            return StartWindowsTerminal(wslWorkspacePath, shellCommand);
        }

        private static Process? StartWindowsTerminal(string wslWorkspacePath, string shellCommand)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wt.exe",
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add("wsl.exe");
            startInfo.ArgumentList.Add("--cd");
            startInfo.ArgumentList.Add(wslWorkspacePath);
            startInfo.ArgumentList.Add("--exec");
            startInfo.ArgumentList.Add("bash");
            startInfo.ArgumentList.Add("-ic");
            startInfo.ArgumentList.Add(shellCommand);

            return Process.Start(startInfo);
        }

        private IReadOnlyList<string> GetWslDistributionNames()
        {
            if (_wslDistributionNames is not null)
            {
                return _wslDistributionNames;
            }

            var names = new List<string>();
            var environmentDistributionName = Environment.GetEnvironmentVariable("WSL_DISTRO_NAME");
            if (!string.IsNullOrWhiteSpace(environmentDistributionName))
            {
                names.Add(environmentDistributionName.Trim());
            }

            if (OperatingSystem.IsWindows()
                && TryRunWindowsProcess("wsl.exe", new[] { "--list", "--quiet" }, out var output))
            {
                foreach (var line in SplitProcessOutputLines(output))
                {
                    if (!names.Contains(line, StringComparer.OrdinalIgnoreCase))
                    {
                        names.Add(line);
                    }
                }
            }

            _wslDistributionNames = names.ToArray();
            return _wslDistributionNames;
        }

        private static bool TryRunWslCommand(string command, out string output)
        {
            return TryRunWindowsProcess(
                "wsl.exe",
                new[] { "--exec", "sh", "-lc", command },
                out output);
        }

        private static bool TryRunWindowsProcess(
            string fileName,
            IReadOnlyList<string> arguments,
            out string output)
        {
            output = string.Empty;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return false;
                }

                if (!process.WaitForExit(WslCommandTimeoutMilliseconds))
                {
                    process.Kill(entireProcessTree: true);
                    return false;
                }

                output = process.StandardOutput.ReadToEnd();
                return process.ExitCode == 0;
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or Win32Exception)
            {
                output = string.Empty;
                return false;
            }
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
            return output.Replace("\0", string.Empty);
        }

        private static string? NormalizeWorkspacePathForComparison(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var normalizedPath = path.Trim().Replace('\\', '/');
            if (normalizedPath.Length >= 2
                && char.IsLetter(normalizedPath[0])
                && normalizedPath[1] == ':')
            {
                normalizedPath = "/mnt/"
                    + char.ToLowerInvariant(normalizedPath[0])
                    + normalizedPath[2..];
            }

            if (normalizedPath.Length >= 7
                && normalizedPath.StartsWith("/mnt/", StringComparison.Ordinal)
                && char.IsLetter(normalizedPath[5])
                && normalizedPath[6] == '/')
            {
                normalizedPath = "/mnt/"
                    + char.ToLowerInvariant(normalizedPath[5])
                    + normalizedPath[6..];
            }

            return TrimTrailingSlashes(normalizedPath);
        }

        private static bool IsMountedWindowsDrivePath(string path)
        {
            return path.Length >= 6
                && path.StartsWith("/mnt/", StringComparison.Ordinal)
                && char.IsLetter(path[5])
                && (path.Length == 6 || path[6] == '/');
        }

        private static string TrimTrailingSlashes(string path)
        {
            var minimumLength = path == "/" ? 1 : 0;
            if (IsMountedWindowsDrivePath(path))
            {
                minimumLength = 6;
            }

            var end = path.Length;
            while (end > minimumLength && path[end - 1] == '/')
            {
                end--;
            }

            return end == path.Length ? path : path[..end];
        }

        private static string BuildShellCommand(string executableName, IEnumerable<string> arguments)
        {
            return string.Join(
                " ",
                new[] { QuoteBashArgument(executableName) }
                    .Concat(arguments.Select(QuoteBashArgument)));
        }

        private static string QuoteBashArgument(string value)
        {
            if (value.Length == 0)
            {
                return "''";
            }

            return "'" + value.Replace("'", "'\\''") + "'";
        }

        private static string ConvertToWslPath(string path)
        {
            if (path.StartsWith('/'))
            {
                return path;
            }

            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':')
            {
                return path.Replace('\\', '/');
            }

            var builder = new StringBuilder();
            builder.Append("/mnt/");
            builder.Append(char.ToLowerInvariant(root[0]));

            var relativePath = path[root.Length..].TrimStart('\\', '/');
            if (relativePath.Length > 0)
            {
                builder.Append('/');
                builder.Append(relativePath.Replace('\\', '/'));
            }

            return builder.ToString();
        }
    }
}
