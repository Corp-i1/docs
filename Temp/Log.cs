using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace C1Logger
{
    /// <summary>
    /// Thread-safe, extensible logger with file rolling, async logging, and custom sinks.
    /// </summary>
    public static class C1Log
    {
        private static readonly object _lock = new();
        private static string _logDirectory;
        private static string _logFilePath;
        private static long _maxFileSizeBytes = 10 * 1024 * 1024; // 10 MB default
        private static LogLevel _minLogLevel = LogLevel.Informational;
        private static Func<DateTime, LogLevel, string, string> _formatter = DefaultFormatter;
        private static readonly List<ILogSink> _customSinks = new();
        private static readonly BlockingCollection<(string, LogLevel)> _logQueue = new();
        private static readonly CancellationTokenSource _cts = new();
        private static Task _workerTask;
        private static bool _initialized = false;

        // Add a flag to prevent multiple disposal
        private static bool _disposed = false;

        /// <summary>
        /// Event triggered when a logging failure occurs.
        /// </summary>
        public static event Action<Exception> OnLoggingFailure;

        /// <summary>
        /// Maximum number of log files to keep.
        /// </summary>
        public static int MaxLogFiles { get; set; } = 10;

        /// <summary>
        /// Gets or sets the directory where log files are written.
        /// If not set, defaults to the application's base directory + "Logs".
        /// </summary>
        public static string LogDirectory
        {
            get => _logDirectory ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            set
            {
                lock (_lock)
                {
                    if (_initialized)
                        throw new InvalidOperationException("Cannot change LogDirectory after logging has started.");
                    _logDirectory = value;
                    _logFilePath = null;
                }
            }
        }

        /// <summary>
        /// Gets or sets the maximum log file size in bytes before rolling.
        /// </summary>
        public static long MaxFileSizeBytes
        {
            get => _maxFileSizeBytes;
            set => _maxFileSizeBytes = value > 0 ? value : 10 * 1024 * 1024;
        }

        /// <summary>
        /// Gets or sets the minimum log level to write.
        /// </summary>
        public static LogLevel MinLogLevel
        {
            get => _minLogLevel;
            set => _minLogLevel = value;
        }

        /// <summary>
        /// Gets or sets the log output mode (Console, File, Both).
        /// </summary>
        public static LogOutputMode OutputMode { get; set; } = LogOutputMode.Both;

        /// <summary>
        /// Gets or sets the log message formatter.
        /// </summary>
        public static Func<DateTime, LogLevel, string, string> Formatter
        {
            get => _formatter;
            set => _formatter = value ?? DefaultFormatter;
        }

        /// <summary>
        /// Adds a custom log sink.
        /// </summary>
        public static void AddSink(ILogSink sink)
        {
            if (sink != null)
            {
                lock (_customSinks)
                {
                    _customSinks.Add(sink);
                }
            }
        }

        /// <summary>
        /// Initializes the logger with default settings.
        /// </summary>
        [Obsolete("Manual initialization is no longer required. Configuration should be set before the first log call.")]
        public static void InitLog()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                InitializeLogDirectory();
                CleanUpOldLogFiles();
                _workerTask = Task.Factory.StartNew(
                    () => ProcessLogQueueAsync().GetAwaiter().GetResult(),
                    TaskCreationOptions.LongRunning
                );
                _initialized = true;
                C1Log.Debug("Log.Initialised");
            }
        }

        /// <summary>
        /// Initializes the logger with custom max number log files and log directory.
        /// </summary>
        public static void InitLog(int maxLogFiles, string logDirectory)
        {
            MaxLogFiles = maxLogFiles;
            LogDirectory = logDirectory;
            InitLog();
        }

        /// <summary>
        /// Initializes the logger with custom max number log files.
        /// </summary>
        public static void InitLog(int maxLogFiles)
        {
            MaxLogFiles = maxLogFiles;
            InitLog();
        }


        /// <summary>
        /// Initializes the logger with a custom log directory.
        /// </summary>
        public static void InitLog(string logDirectory)
        {
            LogDirectory = logDirectory;
            InitLog();
        }

        /// <summary>
        /// Initializes the logger with a custom minimum log level.
        /// </summary>
        public static void InitLog(LogLevel minLogLevel)
        {
            MinLogLevel = minLogLevel;
            InitLog();
        }

        /// <summary>
        /// Initializes the logger with a custom log directory and minimum log level.
        /// </summary>
        public static void InitLog(string logDirectory, LogLevel minLogLevel)
        {
            LogDirectory = logDirectory;
            MinLogLevel = minLogLevel;
            InitLog();
        }

        /// <summary>
        /// Initializes the logger with custom max log files, log directory, and minimum log level.
        /// </summary>
        public static void InitLog(int maxLogFiles, string logDirectory, LogLevel minLogLevel)
        {
            MaxLogFiles = maxLogFiles;
            LogDirectory = logDirectory;
            MinLogLevel = minLogLevel;
            InitLog();
        }

        /// <summary>
        /// Initializes the logger with a configuration object.
        /// </summary>
        public static void InitLog(C1LogConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            MaxLogFiles = config.MaxLogFiles;
            if (config.LogDirectory != null) LogDirectory = config.LogDirectory;
            if (config.Formatter != null) Formatter = config.Formatter;
            MinLogLevel = config.MinLogLevel;
            foreach (var sink in config.Sinks) AddSink(sink);
            InitLog();
        }

        public static void Informational(string message) => WriteLog(message, LogLevel.Informational);
        public static void Warning(string message) => WriteLog(message, LogLevel.Warning);
        public static void Error(string message) => WriteLog(message, LogLevel.Error);

        public static void Exception(Exception ex)
        {
            var exceptionMessage = $"Exception: {ex.Message}\nStack Trace: {ex.StackTrace}";
            WriteLog(exceptionMessage, LogLevel.Alert);
        }
        public static void Debug(string message) => WriteLog(message, LogLevel.Debug);
        public static void Notice(string message) => WriteLog(message, LogLevel.Notice);
        public static void Critical(string message) => WriteLog(message, LogLevel.Critical);
        public static void Alert(string message) => WriteLog(message, LogLevel.Alert);
        public static void Emergency(string message) => WriteLog(message, LogLevel.Emergency);

        private static void WriteLog(string message, LogLevel level)
        {
            if (level > MinLogLevel) return;
            if (!_initialized) InitLog();
            _logQueue.Add((message, level));
        }

        private static async Task ProcessLogQueueAsync()
        {
            foreach (var (message, level) in _logQueue.GetConsumingEnumerable(_cts.Token))
            {
                var now = DateTime.Now;
                var logMessage = Formatter(now, level, message);

                // Console/File/Both
                try
                {
                    if (OutputMode == LogOutputMode.Console || OutputMode == LogOutputMode.Both)
                    {
                        Console.WriteLine(logMessage);
                    }
                    if (OutputMode == LogOutputMode.File || OutputMode == LogOutputMode.Both)
                    {
                        WriteToFile(logMessage, now);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[C1Log] Logging failure: {ex}");
                    OnLoggingFailure?.Invoke(ex);
                }

                // Custom sinks (async, filter by MinLogLevel)
                List<Task> sinkTasks = new();
                lock (_customSinks)
                {
                    foreach (var sink in _customSinks)
                    {
                        if (level <= sink.MinLogLevel)
                        {
                            try
                            {
                                sinkTasks.Add(sink.WriteAsync(logMessage, level));
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[C1Log] Logging failure: {ex}");
                                OnLoggingFailure?.Invoke(ex);
                            }
                        }
                    }
                }
                try
                {
                    await Task.WhenAll(sinkTasks);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[C1Log] Logging failure: {ex}");
                    OnLoggingFailure?.Invoke(ex);
                }
            }
        }

        public static void Flush()
        {
            // Signal that no more items will be added
            _logQueue.CompleteAdding();

            // Wait for the worker to finish processing
            _workerTask?.Wait();
            _workerTask?.Dispose();

            lock (_customSinks)
            {
                foreach (var sink in _customSinks)
                {
                    try { sink.Dispose(); } catch { }
                }
                _customSinks.Clear();
            }
        }

        private static void WriteToFile(string logMessage, DateTime now)
        {
            lock (_lock)
            {
                RollLogFileIfNeeded(now);
                File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
            }
        }

        private static void RollLogFileIfNeeded(DateTime now)
        {
            // Roll by date (new file per day) or by size
            var currentFile = LogFilePath;
            var fileDate = File.Exists(currentFile)
                ? File.GetCreationTime(currentFile).Date
                : now.Date;

            if (fileDate != now.Date || (File.Exists(currentFile) && new FileInfo(currentFile).Length > MaxFileSizeBytes))
            {
                _logFilePath = Path.Combine(LogDirectory, $"log_{now:yyyy-MM-dd_HH-mm-ss}.txt");
            }
        }

        private static void InitializeLogDirectory()
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
                // Don't log here to avoid recursion
            }
        }

        private static void CleanUpOldLogFiles()
        {
            var oldFiles = Directory.GetFiles(LogDirectory, "log_*.txt")
                                    .OrderByDescending(File.GetCreationTime)
                                    .Skip(MaxLogFiles);

            foreach (var file in oldFiles)
            {
                try { File.Delete(file); }
                catch { }
            }
        }

        private static string LogFilePath
        {
            get
            {
                if (_logFilePath == null)
                {
                    lock (_lock)
                    {
                        if (_logFilePath == null)
                        {
                            _logFilePath = Path.Combine(LogDirectory, $"log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
                        }
                    }
                }
                return _logFilePath;
            }
        }

        private static string DefaultFormatter(DateTime timestamp, LogLevel level, string message)
            => $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";

        public enum LogOutputMode
        {
            Console,
            File,
            Both
        }

        /// <summary>
        /// Interface for custom log sinks.
        /// </summary>
        public interface ILogSink : IDisposable
        {
            /// <summary>
            /// The minimum log level this sink will process.
            /// </summary>
            LogLevel MinLogLevel { get; }

            /// <summary>
            /// Write a log message asynchronously.
            /// </summary>
            Task WriteAsync(string message, LogLevel level, IDictionary<string, object> context = null);
        }

        public enum LogLevel
        {
            Emergency,     // System is unusable
            Alert,         // Immediate action required
            Critical,      // Critical conditions
            Error,         // Error conditions
            Warning,       // Warning conditions
            Notice,        // Normal but significant condition
            Informational, // Informational messages
            Debug          // Debug-level messages
        }
    }

    public static class LogLevelExtensions
    {
        /// <summary>
        /// Returns the syslog-style shorthand for each log level.
        /// </summary>
        public static string ShortHand(this C1Log.LogLevel level) => level switch
        {
            C1Log.LogLevel.Emergency => "emerg",
            C1Log.LogLevel.Alert => "alert",
            C1Log.LogLevel.Critical => "crit",
            C1Log.LogLevel.Error => "error",
            C1Log.LogLevel.Warning => "warn",
            C1Log.LogLevel.Notice => "notice",
            C1Log.LogLevel.Informational => "info",
            C1Log.LogLevel.Debug => "debug",
            _ => level.ToString().ToLowerInvariant()
        };
    }

    /// <summary>
    /// Configuration class for C1Log.
    /// </summary>
    public class C1LogConfig
    {
        public int MaxLogFiles { get; set; } = 10;
        public string LogDirectory { get; set; }
        public C1Log.LogLevel MinLogLevel { get; set; } = C1Log.LogLevel.Informational;
        public Func<DateTime, C1Log.LogLevel, string, string> Formatter { get; set; }
        public List<C1Log.ILogSink> Sinks { get; } = new();
    }
}