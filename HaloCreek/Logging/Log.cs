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

        private static readonly object SyncRoot = new();
        private static StreamWriter? _writer;
        private static string? _currentFilePath;
        private static bool _isShutDown;

        public static string? CurrentFilePath
        {
            get
            {
                lock (SyncRoot)
                {
                    return _currentFilePath;
                }
            }
        }

        public static void InitializeFileOutput()
        {
            InitializeFileOutput(GetDefaultLogDirectoryPath());
        }

        private static void InitializeFileOutput(string logDirectoryPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(logDirectoryPath);

            lock (SyncRoot)
            {
                if (_writer is not null)
                {
                    throw new InvalidOperationException("Log file output has already been initialized.");
                }

                Directory.CreateDirectory(logDirectoryPath);

                var filePath = Path.Combine(logDirectoryPath, CreateLogFileName());
                var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream, Encoding.UTF8)
                {
                    AutoFlush = true,
                };
                _currentFilePath = filePath;
                _isShutDown = false;
            }
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

            lock (SyncRoot)
            {
                if (_isShutDown)
                {
                    return;
                }

                if (_writer is null)
                {
                    throw new InvalidOperationException("Log file output has not been initialized.");
                }

                _writer.WriteLine(FormatLine(level, category, message));
            }
        }

        public static void Shutdown()
        {
            lock (SyncRoot)
            {
                if (_writer is null)
                {
                    _isShutDown = true;
                    return;
                }

                _writer.Flush();
                _writer.Dispose();
                _writer = null;
                _isShutDown = true;
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
