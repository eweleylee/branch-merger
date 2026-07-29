using System.Globalization;
using System.Text;

namespace BranchMerger.Api.Services;

/// <summary>
/// Minimal file logger — no external dependencies. Writes entries at or above a minimum
/// level (default Warning, so it captures errors and merge conflicts/failures) to one
/// file per day: <c>{DataDir}/logs/log-yyyy-MM-dd.txt</c>. Old files are pruned by
/// <see cref="LogMaintenanceBackgroundService"/>. Timestamps/filenames use local time.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly LogLevel _min;
    private readonly object _gate = new();

    public FileLoggerProvider(string logDirectory, LogLevel minimumLevel)
    {
        _dir = logDirectory;
        _min = minimumLevel;
        try { Directory.CreateDirectory(_dir); } catch { /* best effort */ }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);
    public void Dispose() { }

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _min;

    internal void Append(string category, LogLevel level, string message, Exception? ex)
    {
        try
        {
            var now = DateTime.Now;
            var path = Path.Combine(_dir, $"log-{now:yyyy-MM-dd}.txt");
            var sb = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level).Append("] ")
                .Append(category).Append(" - ").Append(message);
            if (ex != null) sb.Append(Environment.NewLine).Append(ex);
            sb.Append(Environment.NewLine);
            lock (_gate) File.AppendAllText(path, sb.ToString());
        }
        catch { /* logging must never throw */ }
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly FileLoggerProvider _provider;

    public FileLogger(string category, FileLoggerProvider provider)
    {
        _category = category;
        _provider = provider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        _provider.Append(_category, logLevel, formatter(state, exception), exception);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>Prunes daily log files older than a retention window. Silent/best-effort.</summary>
public static class LogCleanup
{
    public static void Run(string logDirectory, int retentionDays)
    {
        try
        {
            if (retentionDays <= 0 || !Directory.Exists(logDirectory)) return;
            var cutoff = DateTime.Now.Date.AddDays(-retentionDays);
            foreach (var file in Directory.GetFiles(logDirectory, "log-*.txt"))
            {
                var name = Path.GetFileNameWithoutExtension(file);       // log-2026-07-29
                var datePart = name.Length > 4 ? name[4..] : "";
                if (!DateTime.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var stamp))
                    stamp = File.GetLastWriteTime(file);                 // fallback
                if (stamp.Date < cutoff)
                {
                    try { File.Delete(file); } catch { /* ignore locked/removed */ }
                }
            }
        }
        catch { /* best effort */ }
    }
}
