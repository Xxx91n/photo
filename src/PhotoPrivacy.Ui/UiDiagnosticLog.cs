using Serilog;
using Serilog.Core;

namespace PhotoPrivacy.Ui;

public static class UiDiagnosticLog
{
    private static readonly object Gate = new();
    private static Logger? _logger;

    public static void Initialize(string logDirectory)
    {
        lock (Gate)
        {
            if (_logger is not null)
                return;

            Directory.CreateDirectory(logDirectory);
            _logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Async(a => a.File(
                    System.IO.Path.Combine(logDirectory, "ui-.log"),
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 104857600,
                    retainedFileCountLimit: 31,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"))
                .CreateLogger();
        }
    }

    public static void Write(string message)
    {
        try
        {
            if (_logger is not null)
            {
                _logger.Information(message);
                return;
            }

            // Fallback before Initialize() is called
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "ui-startup.log");
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} | {message}";
            lock (Gate)
            {
                File.AppendAllText(file, line + Environment.NewLine, System.Text.Encoding.UTF8);
            }
        }
        catch
        {
            // best effort diagnostics only
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            _logger?.Dispose();
            _logger = null;
        }
    }
}
