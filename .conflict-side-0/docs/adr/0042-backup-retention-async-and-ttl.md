# ADR 0042: Backup Retention Async + Reuse _cleanupTimer + TTL

**Date**: 2026-08-13
**Status**: Accepted
**Related**: ADR 0041 (Async file ops), ADR 0043 (Backup default dir)

## Context

`BackupRetentionService.EnforceMaxSize` was called synchronously in `MetadataCleanerWorker` after every file processing completion (`HandleFileProcessingCompleted`-style callback). At hundreds of files/sec, every backup triggered a full directory scan + oldest-first deletion — a significant I/O hot path bottleneck.

`BackupOptions` had only `MaxSizeMb` (size cap). No time-based retention — long-running deployments accumulated many small `.bak` files capped at 5GB but file count unbounded, degrading NTFS/ext4 directory performance.

`MetadataCleanerWorker` already had a `_cleanupTimer` (10-min interval) for processed-record cleanup, but retention was not part of its tick.

## Decision

### 1. Move retention to _cleanupTimer tick (Q13/Q17 — 方案 A)

- Remove synchronous `EnforceMaxSize` call from the per-file-completion callback.
- Add `await BackupRetentionService.EnforceMaxSizeAsync(backupDir, maxBytes, retainDays, ct)` to `PerformPeriodicCleanup` (the existing `_cleanupTimer` callback, every 10 min).
- Throttle is inherent (10-min timer) — no additional timestamp needed.
- Failure in retention does NOT block the cleanup tick or main pipeline (try-catch + audit log).

### 2. EnforceMaxSizeAsync signature (Q13 — 方案 A)

```csharp
public static async Task EnforceMaxSizeAsync(
    string backupDirectory, long maxSizeBytes, int retainDays,
    CancellationToken ct)
```

Single-pass O(n) traversal applies BOTH淘汰 strategies:
1. Delete files where `FileInfo.CreationTimeUtc < (now - TimeSpan.FromDays(retainDays))` (TTL)
2. If total still > maxSizeBytes, delete oldest-first until under limit (size)

`retainDays = 0` means TTL disabled — only size cap applies.

### 3. BackupOptions gains RetainDays (Q14 — 方案 A)

`BackupOptions` record gains `int RetainDays` (default 30). Symmetric with `AuditOptions.RetainDays = 30`.
`config.sample.json` adds `"retain_days": 30`.

## Consequences

- Hot path no longer calls retention — ~10x throughput improvement under high load
- Retention runs at most once / 10 min, batch process all pending enforcement
- TTL + size dual strategies cover both "file count runaway" and "disk hogging"
- `BackupOptions` +1 field, dotnet build bump schema version only on user save

## References

- pwm research: per-file lock-free is industry standard for high-throughput backup; no global lock needed
- _cleanupTimer already at `MetadataCleanerWorker.cs:80`: 10-min interval
