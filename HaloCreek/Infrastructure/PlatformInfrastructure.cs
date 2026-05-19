using System;
using System.IO;
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
    }
}
