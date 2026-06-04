using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using HaloCreek.Logging;

namespace HaloCreek.Services
{
    public sealed class DiffService
    {
        public void OpenDiff(string leftPath, string rightPath, string title)
        {
            try
            {
                Log.Info(
                    "Diff",
                    $"Starting external diff. Title={QuoteForLog(title)} "
                    + $"Left={QuoteForLog(leftPath)} Right={QuoteForLog(rightPath)}");

                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "TortoiseGitProc.exe",
                    UseShellExecute = false,
                };
                process.StartInfo.ArgumentList.Add("/command:diff");
                process.StartInfo.ArgumentList.Add($"/path:{rightPath}");
                process.StartInfo.ArgumentList.Add($"/path2:{leftPath}");

                process.Start();
                Log.Info(
                    "Diff",
                    $"Started external diff. Title={QuoteForLog(title)} ProcessId={process.Id}");
            }
            catch (Exception ex) when (ex is Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                Log.Error("Diff", ex, $"Failed to start external diff. Title={QuoteForLog(title)}");
                throw new InvalidOperationException($"Failed to start {title}: {ex.Message}", ex);
            }
        }

        private static string QuoteForLog(string? value)
        {
            return value is null
                ? "<null>"
                : $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }
    }
}
