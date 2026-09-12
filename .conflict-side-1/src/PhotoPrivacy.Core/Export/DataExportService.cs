using System.Text;
using System.Text.Json;

namespace PhotoPrivacy.Core.Export;

public static class DataExportService
{
    public static void ExportAuditLogs(string auditLogDirectory, string exportPath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(exportPath) ?? string.Empty);

        using var writer = new StreamWriter(exportPath, false, Encoding.UTF8);
        writer.WriteLine("event_type,level,timestamp_utc,task_id,source_path_masked,source_path_hash,message");

        if (!Directory.Exists(auditLogDirectory))
            return;

        foreach (var file in Directory.EnumerateFiles(auditLogDirectory, "audit-*.jsonl"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExportSingleAuditFile(file, writer);
        }
    }

    private static void ExportSingleAuditFile(string filePath, StreamWriter writer)
    {
        using var reader = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var eventType = GetString(root, "event_type");
                var level = GetString(root, "level");
                var timestamp = GetString(root, "timestamp_utc");
                var taskId = GetString(root, "task_id");
                var pathMasked = GetString(root, "source_path_masked");
                var pathHash = GetString(root, "source_path_hash");
                var message = GetString(root, "message");

                writer.WriteLine(string.Join(",",
                    CsvEscape(eventType),
                    CsvEscape(level),
                    CsvEscape(timestamp),
                    CsvEscape(taskId),
                    CsvEscape(pathMasked),
                    CsvEscape(pathHash),
                    CsvEscape(message)));
            }
            catch
            {
                // skip malformed lines
            }
        }
    }

    public static void ExportProcessedRecords(string recordStoreDirectory, string exportPath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(exportPath) ?? string.Empty);

        using var writer = new StreamWriter(exportPath, false, Encoding.UTF8);
        writer.WriteLine("key,timestamp_utc");

        var recordPath = Path.Combine(recordStoreDirectory, "processed.ndjson");
        if (!File.Exists(recordPath))
            return;

        using var reader = new StreamReader(new FileStream(recordPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var key = GetString(root, "key");
                var ts = GetString(root, "ts");
                writer.WriteLine(string.Join(",", CsvEscape(key), CsvEscape(ts)));
            }
            catch
            {
                // skip malformed lines
            }
        }
    }

    public static void ExportConfig(string configPath, string exportPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(exportPath) ?? string.Empty);
        File.Copy(configPath, exportPath, overwrite: true);
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
