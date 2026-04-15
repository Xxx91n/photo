using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.Pipeline;

public sealed class FileTaskPipeline
{
    private readonly AppConfig _config;
    private readonly RuleEngine _ruleEngine;
    private readonly IExifToolBridge _bridge;
    private readonly IFileOperations _fileOperations;
    private readonly IAuditLogger _audit;

    private readonly HashSet<string> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public FileTaskPipeline(
        AppConfig config,
        RuleEngine ruleEngine,
        IExifToolBridge bridge,
        IFileOperations fileOperations,
        IAuditLogger audit)
    {
        _config = config;
        _ruleEngine = ruleEngine;
        _bridge = bridge;
        _fileOperations = fileOperations;
        _audit = audit;
    }

    public async Task HandleAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (!TryEnterInflight(sourcePath))
        {
            return;
        }

        try
        {
            var decision = _ruleEngine.Decide(sourcePath);
            if (!decision.ShouldProcess || decision.OutputPath is null)
            {
                await _audit.WriteAsync(
                    new AuditEvent("file_skipped", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, decision.Reason, null),
                    cancellationToken);
                return;
            }

            if (decision.CreateBackup && decision.BackupPath is not null)
            {
                _fileOperations.Copy(sourcePath, decision.BackupPath, overwrite: true);
            }

            var target = decision.OutputPath;
            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                _fileOperations.EnsureDirectory(targetDir);
            }

            for (var attempt = 1; attempt <= _config.Retry.MaxAttempts; attempt++)
            {
                try
                {
                    await _bridge.WipeMetadataAsync(target, cancellationToken);
                    await _audit.WriteAsync(
                        new AuditEvent("file_processing_succeeded", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, $"attempt={attempt}", null),
                        cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    await _audit.WriteAsync(
                        new AuditEvent("file_processing_failed", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, ex.Message, null),
                        cancellationToken);

                    if (attempt < _config.Retry.MaxAttempts)
                    {
                        var delaySeconds = _config.Retry.BackoffSeconds[
                            Math.Min(attempt - 1, _config.Retry.BackoffSeconds.Length - 1)];
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    }
                }
            }

            if (_config.Quarantine.Enabled)
            {
                _fileOperations.EnsureDirectory(_config.Quarantine.Directory);
                var destination = Path.Combine(_config.Quarantine.Directory, Path.GetFileName(sourcePath));
                _fileOperations.Move(sourcePath, destination);
                await _audit.WriteAsync(
                    new AuditEvent("file_quarantined", DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"), sourcePath, destination, null),
                    cancellationToken);
            }
        }
        finally
        {
            ExitInflight(sourcePath);
        }
    }

    private bool TryEnterInflight(string path)
    {
        lock (_gate)
        {
            return _inflight.Add(path);
        }
    }

    private void ExitInflight(string path)
    {
        lock (_gate)
        {
            _inflight.Remove(path);
        }
    }
}
