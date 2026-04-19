using System.Text;

namespace PhotoPrivacy.Ui;

public sealed class AuditTailService
{
    private readonly string _auditDirectory;
    private readonly Action<string> _onLine;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public AuditTailService(string auditDirectory, Action<string> onLine)
    {
        _auditDirectory = auditDirectory;
        _onLine = onLine;
    }

    public void Start()
    {
        _loopTask = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // ignore cancellation races during app shutdown
            }
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var file = GetTodayAuditFile();
            if (!File.Exists(file))
            {
                await Task.Delay(500, token);
                continue;
            }

            await TailFileAsync(file, token);
            await Task.Delay(300, token);
        }
    }

    private async Task TailFileAsync(string filePath, CancellationToken token)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        if (stream.Length > 64 * 1024)
        {
            stream.Seek(-64 * 1024, SeekOrigin.End);
            _ = await reader.ReadLineAsync();
        }

        while (!token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token);
            if (line is not null)
            {
                _onLine(line);
                continue;
            }

            await Task.Delay(200, token);
        }
    }

    private string GetTodayAuditFile()
    {
        var fileName = "audit-" + DateTime.Now.ToString("yyyy-MM-dd") + ".jsonl";
        return Path.Combine(_auditDirectory, fileName);
    }
}
