using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace HaloCreek.Infrastructure
{
    public sealed class PlatformInfrastructure
    {
        private readonly Window _owner;

        public PlatformInfrastructure(Window owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public async Task<string?> SelectDirectoryAsync()
        {
            if (!_owner.StorageProvider.CanPickFolder)
            {
                return null;
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

                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    return localPath;
                }

                return folder.Path.IsFile ? folder.Path.LocalPath : folder.Path.ToString();
            }
            finally
            {
                foreach (var folder in folders)
                {
                    folder.Dispose();
                }
            }
        }

        // TODO ???????path util
        public string NormalizeDirectoryPath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            return Path.GetFullPath(path.Trim());
        }

        public bool IsValidDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                var normalizedPath = NormalizeDirectoryPath(path);

                return Directory.Exists(normalizedPath);
            }
            catch (Exception ex) when (ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public bool TryNormalizeValidDirectoryPath(string? path, out string normalizedPath)
        {
            normalizedPath = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                normalizedPath = NormalizeDirectoryPath(path);

                return Directory.Exists(normalizedPath);
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
