using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace HaloCreek.Logging
{
    // Usage:
    // using HaloCreek.Logging;
    //
    // Log.Info("Application", "message");
    // Log.Warning("Git", "message");
    // Log.Error("Session", ex, "failed to launch session");
    //
    // The category is a short subsystem name used to group log entries.
    public static class Log
    {
        private const string DefaultApplicationName = "HaloCreek";
        private const string DefaultLogDirectoryName = "Logs";

        private static readonly object WriterLock = new();
        private static StreamWriter? _writer;

        public static void InitializeFileOutput()
        {
            var logDirectoryPath = GetDefaultLogDirectoryPath();
            ArgumentException.ThrowIfNullOrWhiteSpace(logDirectoryPath);

            // This is currently called once during application startup. Add explicit state handling
            // if initialization becomes concurrent or repeatable.
            Directory.CreateDirectory(logDirectoryPath);

            var filePath = Path.Combine(logDirectoryPath, CreateLogFileName());
            var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, Encoding.UTF8)
            {
                AutoFlush = true,
            };

            Write(LogLevel.Info, "Application", $"{DefaultApplicationName} starting. LogFile={filePath}");
        }

        public static void Trace(string category, string message)
        {
            Write(LogLevel.Trace, category, message);
        }

        public static void Debug(string category, string message)
        {
            Write(LogLevel.Debug, category, message);
        }

        public static void Info(string category, string message)
        {
            Write(LogLevel.Info, category, message);
        }

        public static void Warning(string category, string message)
        {
            Write(LogLevel.Warning, category, message);
        }

        public static void Error(string category, string message)
        {
            Write(LogLevel.Error, category, message);
        }

        public static void Error(string category, Exception exception, string message)
        {
            ArgumentNullException.ThrowIfNull(exception);

            Write(LogLevel.Error, category, $"{message}{Environment.NewLine}{exception}");
        }

        public static void Write(LogLevel level, string category, string message)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(category);
            ArgumentNullException.ThrowIfNull(message);

            lock (WriterLock)
            {
                if (_writer is null)
                {
                    // Calls outside the normal application lifetime are intentionally ignored.
                    // If callers must distinguish uninitialized from disposed, replace this with
                    // an explicit enum state.
                    return;
                }

                _writer.WriteLine(FormatLine(level, category, message));
            }
        }

        public static void Shutdown()
        {
            lock (WriterLock)
            {
                var writer = _writer;
                System.Diagnostics.Debug.Assert(
                    writer is not null,
                    "Log.Shutdown should only be called once after file output is initialized.");

                writer!.Flush();
                writer.Dispose();
                _writer = null;
            }
        }

        private static string CreateLogFileName()
        {
            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            return $"{DefaultApplicationName}_{timestamp}_{Environment.ProcessId}.log";
        }

        private static string GetDefaultLogDirectoryPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appDataPath))
            {
                appDataPath = AppContext.BaseDirectory;
            }

            return Path.Combine(appDataPath, DefaultApplicationName, DefaultLogDirectoryName);
        }

        private static string FormatLine(LogLevel level, string category, string message)
        {
            var timestamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
            var threadId = Environment.CurrentManagedThreadId;
            return $"{timestamp} [{level}] [{category}] [T{threadId}] {message}";
        }
    }
}
