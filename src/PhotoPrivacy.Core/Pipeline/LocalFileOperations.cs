namespace PhotoPrivacy.Core.Pipeline;

public sealed class LocalFileOperations : IFileOperations
{
    private readonly Action<string, string> _move;
    private readonly Action<string, string, bool> _copy;
    private readonly Action<string> _delete;
    private readonly Action<string> _ensureDirectory;

    public LocalFileOperations(
        Action<string, string>? move = null,
        Action<string, string, bool>? copy = null,
        Action<string>? delete = null,
        Action<string>? ensureDirectory = null)
    {
        _move = move ?? File.Move;
        _copy = copy ?? File.Copy;
        _delete = delete ?? File.Delete;
        _ensureDirectory = ensureDirectory ?? (path => Directory.CreateDirectory(path));
    }

    public void EnsureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _ensureDirectory(path);
    }

    public void Move(string source, string destination)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);

        try
        {
            if (File.Exists(destination))
            {
                _delete(destination);
            }

            _move(source, destination);
        }
        catch (IOException)
        {
            _copy(source, destination, true);
            _delete(source);
        }
    }

    public void Copy(string source, string destination, bool overwrite)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
        _copy(source, destination, overwrite);
    }
    public void AtomicCopy(string source, string destination, bool overwrite)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);

        // Random suffix on temp to prevent concurrent collisions
        var tempPath = destination + ".tmp." + Path.GetRandomFileName();

        _copy(source, tempPath, true);

        if (overwrite && File.Exists(destination))
        {
            // File.Replace is cross-platform atomic (Windows ReplaceFile, Unix rename(2))
            File.Replace(tempPath, destination, null, true);
        }
        else
        {
            File.Move(tempPath, destination);
        }
    }

    // 崩溃一致性（#37）取舍：只做用户态 FlushAsync 时，掉电窗口内 File.Replace 会把半截/空文件
    // 换上位，而原件随后被清洗，备份这条唯一安全副本的损失不可逆，故复制完成后先 FlushToDisk
    // 再交 AtomicCopyAsync 原子替换；fsync 相对整文件复制成本可忽略。处理记录
    // （FileProcessedRecordStore）与审计日志（JsonLineAuditLogger）登记不做：丢失后果是幂等
    // 重扫/可容忍的审计缺口，逐条 fsync 与热目录吞吐不成比例，取舍登记在 #37。
    // Design(#37): 备份副本必须 Flush(flushToDisk: true) 落盘后才允许原子替换，禁退回用户态 flush。
    public async Task CopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
        const int bufferSize = 65536;
        using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous);
        using var dst = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous);
        await src.CopyToAsync(dst, bufferSize, cancellationToken).ConfigureAwait(false);
        await dst.FlushAsync(cancellationToken).ConfigureAwait(false);
        dst.Flush(flushToDisk: true);
    }

    public async Task AtomicCopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
        var tempPath = destination + ".tmp." + Path.GetRandomFileName();
        await CopyAsync(source, tempPath, overwrite: true, cancellationToken).ConfigureAwait(false);
        // File.Move is metadata-only; wrapping in Task.CompletedTask for interface uniformity
        if (overwrite && File.Exists(destination))
        {
            File.Replace(tempPath, destination, null, true);
        }
        else
        {
            File.Move(tempPath, destination);
        }
    }

    public Task MoveAsync(string source, string destination, CancellationToken cancellationToken)
    {
        // Move is metadata-only (atomic on same volume), not I/O bound — wrap sync
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
        Move(source, destination);
        return Task.CompletedTask;
    }

}
