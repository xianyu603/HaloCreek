using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

        public static IEnumerable<string> ReadTextFileLinesWithWriteSharing(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            using var stream = OpenFileForReadWithWriteSharing(filePath);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                yield return line;
            }
        }

        public static FileStream OpenFileForReadWithWriteSharing(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            return new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
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

        public static bool TryRunWindowsCommand(
            string executableName,
            IEnumerable<string> arguments,
            out string output)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
            ArgumentNullException.ThrowIfNull(arguments);

            return TryRunWindowsProcess(executableName, arguments.ToArray(), out output);
        }

        public static string ConvertPathToWindows(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            return NormalizeExternalShellPath(path);
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

        public static void OpenMarkdownLink(Uri href)
        {
            ArgumentNullException.ThrowIfNull(href);

            var target = href.IsAbsoluteUri && href.IsFile
                ? href.LocalPath
                : href.OriginalString.Trim();
            ArgumentException.ThrowIfNullOrWhiteSpace(target);
            Log.Info(LogCategory, $"Opening markdown link. Target={target}");

            if (StartsWithProtocol(target))
            {
                Log.Info(LogCategory, $"Markdown link uses protocol. Target={target}");
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true,
                });
                return;
            }

            var normalizedTarget = NormalizeExternalShellPath(target);
            var shellTarget = TrimAfterLastPathExtension(normalizedTarget);
            if (TryParseMarkdownFileLineTarget(normalizedTarget, out var fileLineTarget))
            {
                shellTarget = TrimAfterLastPathExtension(fileLineTarget.Path);
                var editorPath = QueryDefaultExecutableForPath(shellTarget);
                if (editorPath is not null
                    && WorkspaceRuntime.Current.EffectiveConfig.MarkdownLineJumpCommands.TryGetValue(
                        Path.GetFileNameWithoutExtension(editorPath) ?? string.Empty,
                        out var argumentTemplates))
                {
                    StartProcess(
                        editorPath,
                        argumentTemplates
                            .Select(argument => ResolveMarkdownLineJumpArgument(
                                argument,
                                fileLineTarget with { Path = shellTarget }))
                            .ToArray());
                    return;
                }
            }

            using var fileProcess = Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(shellTarget),
                UseShellExecute = true,
            });
        }

        public static int StartCurrentApplication(string workspacePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);

            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                throw new InvalidOperationException("Current application executable could not be resolved.");
            }

            if (IsDotnetHostPath(processPath))
            {
                throw new InvalidOperationException(
                    "Workspace restart requires HaloCreek to be started from its executable.");
            }

            return StartProcess(
                processPath,
                new[] { workspacePath },
                AppContext.BaseDirectory);
        }

        private static bool IsDotnetHostPath(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWindowsRootedPath(string path)
        {
            return path.Length >= 3
                && char.IsAsciiLetter(path[0])
                && path[1] == ':'
                && path[2] == '/';
        }

        private static bool StartsWithProtocol(string target)
        {
            var separatorIndex = target.IndexOf("://", StringComparison.Ordinal);
            if (separatorIndex <= 0 || !char.IsAsciiLetter(target[0]))
            {
                return false;
            }

            for (var index = 1; index < separatorIndex; index++)
            {
                var character = target[index];
                if (!char.IsAsciiLetterOrDigit(character) && character is not '+' and not '-' and not '.')
                {
                    return false;
                }
            }

            return true;
        }

        private static string TrimAfterLastPathExtension(string path)
        {
            var lastBackslashIndex = path.LastIndexOf('\\');
            var lastSlashIndex = path.LastIndexOf('/');
            var fileNameStartIndex = Math.Max(lastBackslashIndex, lastSlashIndex) + 1;
            var extensionStartInFileName = path.AsSpan(fileNameStartIndex).LastIndexOf('.');
            if (extensionStartInFileName < 0)
            {
                return path;
            }

            var extensionEndIndex = fileNameStartIndex + extensionStartInFileName + 1;
            while (extensionEndIndex < path.Length && char.IsAsciiLetterOrDigit(path[extensionEndIndex]))
            {
                extensionEndIndex++;
            }

            return extensionEndIndex == fileNameStartIndex + extensionStartInFileName + 1
                || extensionEndIndex == path.Length
                    ? path
                    : path[..extensionEndIndex];
        }

        private static bool TryParseMarkdownFileLineTarget(
            string target,
            out MarkdownFileLineTarget fileLineTarget)
        {
            fileLineTarget = default;

            var lineSeparatorIndex = target.LastIndexOf(':');
            if (lineSeparatorIndex < 0
                || !int.TryParse(target[(lineSeparatorIndex + 1)..], out var lineOrColumn)
                || lineOrColumn <= 0)
            {
                return false;
            }

            var path = target[..lineSeparatorIndex];
            int? column = null;
            var previousSeparatorIndex = path.LastIndexOf(':');
            if (previousSeparatorIndex >= 0
                && int.TryParse(path[(previousSeparatorIndex + 1)..], out var line)
                && line > 0)
            {
                column = lineOrColumn;
                lineOrColumn = line;
                path = path[..previousSeparatorIndex];
            }

            fileLineTarget = new MarkdownFileLineTarget(
                path,
                lineOrColumn,
                column);

            return true;
        }

        private static string? QueryDefaultExecutableForPath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var extension = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return null;
            }

            return QueryAssociationString(extension);
        }

        private static string ResolveMarkdownLineJumpArgument(
            string argument,
            MarkdownFileLineTarget target)
        {
            return argument
                .Replace("{Path}", Path.GetFullPath(target.Path), StringComparison.Ordinal)
                .Replace("{Line}", target.Line.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        private static string? QueryAssociationString(string extension)
        {
            const int BufferCharacterCount = 4096;

            var characterCount = (uint)BufferCharacterCount;
            var buffer = new StringBuilder(BufferCharacterCount);
            var result = AssocQueryString(
                0,// AssocF.None
                2, // AssocStr.Executable
                extension,
                "open",
                buffer,
                ref characterCount);

            if (result < 0)
            {
                Log.Debug(
                    LogCategory,
                    $"AssocQueryString failed. Extension={extension} HResult=0x{result:X8}");
                return null;
            }

            var value = buffer.ToString().TrimEnd('\0');
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int AssocQueryString(
            int flags,
            int associationString,
            string association,
            string? extra,
            StringBuilder output,
            ref uint outputCharacterCount);

        private readonly record struct MarkdownFileLineTarget(
            string Path,
            int Line,
            int? Column);

        private static string NormalizeExternalShellPath(string path)
        {
            var normalizedPath = Uri.UnescapeDataString(path.Trim());
            if (!OperatingSystem.IsWindows())
            {
                return NormalizePathForCurrentPlatform(normalizedPath);
            }

            var slashPath = normalizedPath.Replace('\\', '/');
            if (slashPath.StartsWith("mnt/", StringComparison.Ordinal))
            {
                slashPath = "/" + slashPath;
            }

            if (IsMountedWindowsDrivePath(slashPath))
            {
                var drive = char.ToUpperInvariant(slashPath[5]);
                return slashPath.Length == 6
                    ? drive + @":\"
                    : drive + ":" + slashPath[6..].Replace('/', '\\');
            }

            return normalizedPath.Replace('/', '\\');
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

        public static bool TryGetCodexDirectoryPath(out string codexDirectoryPath)
        {
            codexDirectoryPath = string.Empty;

            var configuredCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(configuredCodexHome))
            {
                try
                {
                    codexDirectoryPath = Path.GetFullPath(configuredCodexHome.Trim());
                    return true;
                }
                catch (Exception ex) when (ex is ArgumentException
                    or IOException
                    or NotSupportedException
                    or PathTooLongException
                    or UnauthorizedAccessException)
                {
                    Log.Warning(
                        LogCategory,
                        $"CODEX_HOME could not be read. Path={configuredCodexHome}, Error={ex.Message}");
                    return false;
                }
            }

            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfilePath))
            {
                Log.Warning(LogCategory, "Windows user profile directory could not be resolved.");
                return false;
            }

            codexDirectoryPath = Path.Combine(userProfilePath, ".codex");
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

        public Process? LaunchTerminal(TerminalCommandSpec command)
        {
            ArgumentNullException.ThrowIfNull(command);

            if (command is not TerminalWindowsCommandSpec windowsCommand)
            {
                throw new NotSupportedException(
                    "Unsupported terminal command type: " + command.GetType().FullName);
            }

            return StartForegroundProcess(
                ConvertPathToWindows(windowsCommand.WorkingDirectory),
                windowsCommand.WindowTitle,
                windowsCommand.Executable,
                windowsCommand.Arguments);
        }

        private static Process? StartForegroundProcess(
            string workingDirectory,
            string windowTitle,
            string executable,
            IEnumerable<string> arguments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(windowTitle);
            ArgumentException.ThrowIfNullOrWhiteSpace(executable);
            ArgumentNullException.ThrowIfNull(arguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false
            };
            startInfo.Environment["HALOCREEK_TERMINAL_TITLE"] = SanitizeConsoleWindowTitle(windowTitle);

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return Process.Start(startInfo);
        }

        private static string SanitizeConsoleWindowTitle(string title)
        {
            var builder = new StringBuilder(title.Length);
            var previousWasWhitespace = false;
            foreach (var character in title.Trim())
            {
                var outputCharacter = char.IsControl(character)
                    || character is '&' or '|' or '<' or '>' or '^' or '%' or '!' or '"'
                        ? ' '
                        : character;
                if (char.IsWhiteSpace(outputCharacter))
                {
                    if (!previousWasWhitespace)
                    {
                        builder.Append(' ');
                    }

                    previousWasWhitespace = true;
                    continue;
                }

                builder.Append(outputCharacter);
                previousWasWhitespace = false;
            }

            var sanitizedTitle = builder.ToString().Trim();
            if (sanitizedTitle.Length == 0)
            {
                return "HaloCreek Session";
            }

            return sanitizedTitle.Length <= 120
                ? sanitizedTitle
                : sanitizedTitle[..120].TrimEnd();
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
    }
}
