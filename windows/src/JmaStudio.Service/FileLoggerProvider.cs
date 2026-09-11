// Minimal file logging -- the Service had no persistent log output at
// all before the Diagnostics window's "Logs panel" needed something to
// tail. Deliberately NOT a rotating/rolling log (no Serilog dependency
// pulled in just for this) -- truncated once per service start, the
// same "always reflects the most recent run" convention start_all.ps1
// already uses for daemon.log on the Python side, and more than enough
// for a live diagnostics tail.

namespace JmaStudio.Service;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private bool _disposed;

    public string LogFilePath { get; }

    public FileLoggerProvider(string logFilePath)
    {
        LogFilePath = logFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
        _writer = new StreamWriter(new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    // Registered into more than one LoggerFactory (the ad-hoc startup
    // logger, LightbarReactiveManager's, and DI's own), each of which
    // disposes every provider it holds on teardown -- guard against the
    // resulting double-Dispose of one shared StreamWriter.
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _writer.Dispose();
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_category}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            lock (_provider._lock)
            {
                if (!_provider._disposed) _provider._writer.WriteLine(line);
            }
        }
    }
}
