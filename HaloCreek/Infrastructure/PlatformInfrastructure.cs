using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using HaloCreek.Logging;
using HaloCreek.Services;

namespace HaloCreek.Infrastructure
{
    public sealed record PlatformProcessResult(
        bool Succeeded,
        int? ExitCode,
        string Output,
        string ErrorMessage,
        Exception? Exception = null);

    public sealed class PlatformInfrastructure
    {
        private const string LogCategory = "Platform";
        private const int WslCommandTimeoutMilliseconds = 10000;

        private static IReadOnlyList<string>? _wslDistributionNames;

        private readonly Window _owner;

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
            // 暂时使用相同实现 未来Error可能改样式啥的
            await ShowMessageDialogAsync(title, message);
        }

        public async Task ShowMessageDialogAsync(string title, string message)
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
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
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

        // 暂时用null 表示取消 有点潜规则但范围还小
        public async Task<string?> ShowTextInputDialogAsync(
            string title,
            string initialText,
            string confirmText,
            string cancelText)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            ArgumentNullException.ThrowIfNull(initialText);
            ArgumentException.ThrowIfNullOrWhiteSpace(confirmText);
            ArgumentException.ThrowIfNullOrWhiteSpace(cancelText);

            var dialog = new Window
            {
                Title = title,
                Width = 640,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
            };

            var textBox = new TextBox
            {
                Text = initialText,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 180,
                MaxHeight = 320,
            };
            var confirmCommand = new RelayCommand(() => dialog.Close(textBox.Text ?? string.Empty));
            textBox.KeyBindings.Add(new KeyBinding
            {
                Gesture = KeyGesture.Parse("Ctrl+Enter"),
                Command = confirmCommand,
            });

            var cancelButton = new Button
            {
                Content = cancelText,
                MinWidth = 80,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            cancelButton.Click += (_, _) => dialog.Close(null);

            var confirmButton = new Button
            {
                Content = confirmText,
                MinWidth = 80,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Command = confirmCommand,
            };

            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    textBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            cancelButton,
                            confirmButton,
                        },
                    },
                },
            };

            return await dialog.ShowDialog<string?>(_owner);
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

        public static string NormalizePathForCurrentPlatform(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            return OperatingSystem.IsWindows()
                ? path.Trim().Replace('/', '\\')
                : path.Trim().Replace('\\', '/');
        }

        public static string NormalizeGitRelativePath(string relativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            var normalizedPath = relativePath.Trim().Replace('\\', '/');
            if (normalizedPath.StartsWith("/", StringComparison.Ordinal)
                || IsWindowsRootedPath(normalizedPath)
                || Path.IsPathRooted(normalizedPath))
            {
                throw new ArgumentException("Git relative path must not be absolute.", nameof(relativePath));
            }

            var segments = normalizedPath.Split('/');
            if (segments.Any(segment => segment.Length == 0
                || segment == "."
                || segment == ".."))
            {
                throw new ArgumentException("Git relative path contains invalid segments.", nameof(relativePath));
            }

            return normalizedPath;
        }

        public static string CombinePathForCurrentPlatform(string rootPath, string relativePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

            return Path.Combine(
                NormalizePathForCurrentPlatform(rootPath),
                NormalizePathForCurrentPlatform(relativePath));
        }

        public static bool IsExistingFileUnderDirectory(string? rootPath, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            var absolutePath = CombinePathForCurrentPlatform(rootPath, relativePath);
            return File.Exists(absolutePath) && !Directory.Exists(absolutePath);
        }

        public static string WriteTempFile(string desiredFileName, string content)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(desiredFileName);
            ArgumentNullException.ThrowIfNull(content);

            return WriteTempFile(
                desiredFileName,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));
        }

        public static string WriteTempFile(string desiredFileName, byte[] content)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(desiredFileName);
            ArgumentNullException.ThrowIfNull(content);

            var directory = Path.Combine(Path.GetTempPath(), "HaloCreek");
            Directory.CreateDirectory(directory);

            var safeFileName = SanitizeFileNameSegment(desiredFileName);
            if (safeFileName.Length == 0)
            {
                safeFileName = "file";
            }
            var extension = Path.GetExtension(safeFileName);
            var stem = extension.Length == 0
                ? safeFileName
                : safeFileName[..^extension.Length];
            var randomSuffix = Guid.NewGuid().ToString("N")[..8];
            var tempPath = Path.Combine(directory, $"{stem}-{randomSuffix}{extension}");

            // Callers may pass these paths to external tools or prompts after this method returns.
            // Keep them for now; startup or shutdown can later prune old HaloCreek temp files.
            File.WriteAllBytes(tempPath, content);
            return tempPath;
        }

        public static PlatformProcessResult RunProcessWithCapturedOutput(
            string fileName,
            IEnumerable<string> arguments,
            string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environmentVariables = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            ArgumentNullException.ThrowIfNull(arguments);

            try
            {
                using var process = CreateRedirectedProcess(
                    fileName,
                    arguments,
                    workingDirectory,
                    environmentVariables);
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                return new PlatformProcessResult(
                    process.ExitCode == 0,
                    process.ExitCode,
                    output,
                    error);
            }
            catch (Exception ex) when (ex is Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
            {
                return new PlatformProcessResult(false, null, string.Empty, ex.Message, ex);
            }
        }

        public static async IAsyncEnumerable<string> StreamNullSeparatedProcessOutput(
            string fileName,
            IEnumerable<string> arguments,
            string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environmentVariables = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            ArgumentNullException.ThrowIfNull(arguments);

            using var process = CreateRedirectedProcess(
                fileName,
                arguments,
                workingDirectory,
                environmentVariables);

            try
            {
                process.Start();
            }
            catch (Exception ex) when (ex is Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"Process failed to start. FileName={fileName}", ex);
            }

            using var cancellationRegistration = cancellationToken.Register(
                static state => KillProcessTree((Process)state!),
                process);

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var tokenBuilder = new StringBuilder();
            var buffer = new char[4096];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var readCount = await process.StandardOutput.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken).ConfigureAwait(false);
                if (readCount == 0)
                {
                    break;
                }

                var tokenStart = 0;
                for (var index = 0; index < readCount; index++)
                {
                    if (buffer[index] != '\0')
                    {
                        continue;
                    }

                    tokenBuilder.Append(buffer, tokenStart, index - tokenStart);
                    if (tokenBuilder.Length > 0)
                    {
                        yield return tokenBuilder.ToString();
                        tokenBuilder.Clear();
                    }

                    tokenStart = index + 1;
                }

                if (tokenStart < readCount)
                {
                    tokenBuilder.Append(buffer, tokenStart, readCount - tokenStart);
                }
            }

            if (tokenBuilder.Length > 0)
            {
                yield return tokenBuilder.ToString();
            }

            var errorMessage = await stderrTask.ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Process exited with code {process.ExitCode}. FileName={fileName}, Error={errorMessage.Trim()}");
            }
        }

        private static Process CreateRedirectedProcess(
            string fileName,
            IEnumerable<string> arguments,
            string? workingDirectory,
            IReadOnlyDictionary<string, string?>? environmentVariables)
        {
            var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (environmentVariables is not null)
            {
                foreach (var pair in environmentVariables)
                {
                    process.StartInfo.Environment[pair.Key] = pair.Value;
                }
            }

            return process;
        }

        private static void KillProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or Win32Exception
                or NotSupportedException)
            {
                Log.Warning(LogCategory, $"Failed to kill process tree. Error={ex.Message}");
            }
        }

        public static bool IsExecutableOnPath(string executableName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

            return RunProcessWithCapturedOutput(
                    "where.exe",
                    new[] { executableName })
                .Succeeded;
        }

        public static int StartProcess(
            string fileName,
            IEnumerable<string> arguments,
            string? workingDirectory = null,
            bool createNoWindow = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            ArgumentNullException.ThrowIfNull(arguments);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = createNoWindow,
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                process.StartInfo.WorkingDirectory = workingDirectory;
            }

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            return process.Id;
        }

        private static bool IsWindowsRootedPath(string path)
        {
            return path.Length >= 3
                && char.IsAsciiLetter(path[0])
                && path[1] == ':'
                && path[2] == '/';
        }

        public static bool TryGetWslEnvironmentVariable(string name, out string value)
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
            if (!TryRunWslShellCommand(command, out var output))
            {
                return false;
            }

            value = NormalizeProcessOutput(output).Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        public static bool TryGetReadableWslHomeDirectoryPath(out string homeDirectoryPath)
        {
            homeDirectoryPath = string.Empty;

            if (!TryGetWslEnvironmentVariable("HOME", out var wslHomeDirectoryPath))
            {
                Log.Warning(LogCategory, "WSL HOME could not be resolved.");
                return false;
            }

            if (!TryConvertWslPathToReadablePath(wslHomeDirectoryPath, out var readableHomeDirectoryPath))
            {
                Log.Warning(
                    LogCategory,
                    $"WSL HOME could not be converted to a readable path. WslHome={wslHomeDirectoryPath}");
                return false;
            }

            if (!Directory.Exists(readableHomeDirectoryPath))
            {
                Log.Warning(
                    LogCategory,
                    $"Converted WSL HOME directory does not exist. WslHome={wslHomeDirectoryPath}, ReadablePath={readableHomeDirectoryPath}");
                return false;
            }

            homeDirectoryPath = readableHomeDirectoryPath;
            return true;
        }

        public static bool TryConvertWslPathToReadablePath(string wslPath, out string readablePath)
        {
            readablePath = string.Empty;

            if (string.IsNullOrWhiteSpace(wslPath))
            {
                return false;
            }

            var normalizedWslPath = wslPath.Trim().Replace('\\', '/');
            if (!normalizedWslPath.StartsWith("/", StringComparison.Ordinal)
                || !OperatingSystem.IsWindows())
            {
                readablePath = normalizedWslPath;
                return true;
            }

            var distributionName = GetWslDistributionNames().FirstOrDefault();
            if (string.IsNullOrWhiteSpace(distributionName))
            {
                Log.Warning(
                    LogCategory,
                    $"WSL path could not be converted because no WSL distribution was resolved. WslPath={wslPath}");
                return false;
            }

            readablePath = @"\\wsl.localhost\" + distributionName + normalizedWslPath.Replace('/', '\\');
            return true;
        }

        public bool TryGetWslFileLastWriteTimeUtc(
            string wslPath,
            out DateTimeOffset lastWriteTimeUtc)
        {
            lastWriteTimeUtc = default;

            if (string.IsNullOrWhiteSpace(wslPath))
            {
                return false;
            }

            if (!TryConvertWslPathToReadablePath(wslPath, out var readablePath))
            {
                return false;
            }

            try
            {
                if (!File.Exists(readablePath))
                {
                    return false;
                }

                lastWriteTimeUtc = new DateTimeOffset(
                    File.GetLastWriteTimeUtc(readablePath),
                    TimeSpan.Zero);
                return true;
            }
            catch (Exception ex) when (ex is IOException
                or NotSupportedException
                or UnauthorizedAccessException
                or ArgumentException
                or PathTooLongException)
            {
                return false;
            }
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

            return StartWindowsTerminal(null, wslWorkspacePath, shellCommand);
        }

        public string ConvertPathToWsl(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            return ConvertToWslPath(path);
        }

        public string BuildWslShellCommand(
            string executableName,
            IEnumerable<string> arguments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
            ArgumentNullException.ThrowIfNull(arguments);

            return BuildShellCommand(executableName, arguments);
        }

        public string QuoteWslShellArgument(string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            return QuoteBashArgument(value);
        }

        public bool TryRunWslCommand(
            string executableName,
            IEnumerable<string> arguments,
            out string output)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
            ArgumentNullException.ThrowIfNull(arguments);

            return TryRunWslShellCommand(
                BuildShellCommand(executableName, arguments),
                out output);
        }

        // TODO 这个类的命名需要整理
        public Process? LaunchTerminal(TerminalLaunchRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(request.Command);
            var command = MaterializeTerminalCommand(request.Command);
            ArgumentException.ThrowIfNullOrWhiteSpace(command.WorkingDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(command.Executable);

            var wslWorkspacePath = ConvertToWslPath(command.WorkingDirectory);
            var shellCommand = BuildShellCommand(
                command.Executable,
                command.Arguments);

            var windowIdentity = request.WindowMode == TerminalWindowMode.ReuseOrCreateNamedWindow
                ? request.WindowIdentity
                : null;

            return StartWindowsTerminal(windowIdentity, wslWorkspacePath, shellCommand);
        }

        private static TerminalExecutableCommandSpec MaterializeTerminalCommand(TerminalCommandSpec command)
        {
            return command switch
            {
                TerminalExecutableCommandSpec executableCommand => executableCommand,
                TerminalWslScriptCommandSpec scriptCommand => WriteWslScriptCommand(scriptCommand),
                _ => throw new NotSupportedException(
                    "Unsupported terminal command type: " + command.GetType().FullName)
            };
        }

        private static TerminalExecutableCommandSpec WriteWslScriptCommand(
            TerminalWslScriptCommandSpec command)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(command.WorkingDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(command.FileNameHint);
            ArgumentException.ThrowIfNullOrWhiteSpace(command.ScriptText);

            var fileName = SanitizeFileNameSegment(command.FileNameHint);
            if (fileName.Length == 0)
            {
                fileName = "script";
            }
            if (!fileName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".sh";
            }

            var scriptPath = WriteTempFile(fileName, command.ScriptText);

            return new TerminalExecutableCommandSpec(
                command.WorkingDirectory,
                "bash",
                new[] { ConvertToWslPath(scriptPath) });
        }

        public Process? ActivateTerminalWindow(string windowIdentity)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(windowIdentity);

            return StartWindowsTerminal(windowIdentity, null, null, focusExistingTab: true);
        }

        private static Process? StartWindowsTerminal(
            string? windowIdentity,
            string? wslWorkspacePath,
            string? shellCommand,
            bool focusExistingTab = false)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wt.exe",
                UseShellExecute = false
            };

            if (!string.IsNullOrWhiteSpace(windowIdentity))
            {
                startInfo.ArgumentList.Add("--window");
                startInfo.ArgumentList.Add(windowIdentity);
            }

            if (focusExistingTab)
            {
                startInfo.ArgumentList.Add("ft");
            }

            if (!string.IsNullOrWhiteSpace(wslWorkspacePath)
                && shellCommand is not null)
            {
                startInfo.ArgumentList.Add("wsl.exe");
                startInfo.ArgumentList.Add("--cd");
                startInfo.ArgumentList.Add(wslWorkspacePath);
                startInfo.ArgumentList.Add("--exec");
                startInfo.ArgumentList.Add("bash");
                startInfo.ArgumentList.Add("-ic");
                startInfo.ArgumentList.Add(shellCommand);
            }

            return Process.Start(startInfo);
        }

        private static IReadOnlyList<string> GetWslDistributionNames()
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

        private static bool TryRunWslShellCommand(string command, out string output)
        {
            var fileName = OperatingSystem.IsWindows() ? "wsl.exe" : "bash";
            var arguments = OperatingSystem.IsWindows()
                ? new[] { "--exec", "bash", "-ic", command }
                : new[] { "-ic", command };

            return TryRunWindowsProcess(fileName, arguments, out output);
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

                var standardOutput = process.StandardOutput.ReadToEnd();
                var standardError = process.StandardError.ReadToEnd();
                if (process.ExitCode == 0)
                {
                    output = standardOutput;
                    return true;
                }

                output = standardOutput + standardError;
                return false;
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

        private static string SanitizeFileNameSegment(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append((char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_' or '.')
                        ? character
                        : '-');
            }

            var segment = builder.ToString().Trim('-', '.', '_');
            if (segment.Length == 0)
            {
                return string.Empty;
            }

            return segment.Length <= 120 ? segment : segment[..120];
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
