using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TawkType.Desktop.Logging;

/// <summary>
/// Minimal rolling file logger for a tray app that has no console.
///
/// Two files, never more: <c>tawktype.log</c> is rotated onto <c>tawktype.log.1</c> once it passes
/// 5 MB, and the previous <c>.1</c> is overwritten. So the ceiling on disk is a little over 10 MB —
/// the live file can exceed 5 MB by the one line that tipped it — and nothing ever grows past that.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 5 * 1024 * 1024;

    private readonly string _path;
    private readonly object _gate = new();

    public FileLoggerProvider(string directory, bool enabled)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "tawktype.log");
        Enabled = enabled;
    }

    /// <summary>
    /// Whether anything is written at all. Set from the user's settings before the first line, and
    /// again whenever they save, so switching it off takes effect without a restart — and off means
    /// no line is written, not "written and then deleted later".
    /// </summary>
    public bool Enabled { get; set; }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, ShortName(categoryName));

    public void Dispose()
    {
    }

    private static string ShortName(string category)
    {
        var dot = category.LastIndexOf('.');
        return dot >= 0 ? category[(dot + 1)..] : category;
    }

    private void Write(string line)
    {
        if (!Enabled)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                {
                    File.Move(_path, _path + ".1", overwrite: true);
                }

                File.AppendAllText(_path, line, Encoding.UTF8);
            }
            catch
            {
                // Logging must never take the app down.
            }
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

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var builder = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(Level(logLevel)).Append("] ")
                .Append(_category).Append(": ")
                .Append(formatter(state, exception));

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            _provider.Write(builder.AppendLine().ToString());
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}
