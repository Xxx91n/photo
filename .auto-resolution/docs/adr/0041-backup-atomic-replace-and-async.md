# ADR 0041: Backup Atomic Replace via File.Replace + Async IFileOperations

**Date**: 2026-08-13
**Status**: Accepted
**Related**: ADR 0043 (Backup default dir), ADR 0042 (Retention async)

## Context

`LocalFileOperations.AtomicCopy` used `File.Move(tempPath, destination, overwrite: true)`. While functional, `File.Move(overwrite:true)` on .NET 8+ is copy+delete not true atomic — it has a tiny crash window where destination may be deleted but temp not yet moved.

`IFileOperations` was fully synchronous (`void Move`, `void Copy`, `void AtomicCopy`). `FileTaskPipeline.HandleAsync` called these synchronously in the hot path, blocking thread-pool threads. At hundreds of files/sec this is a throughput bottleneck.

## Decision

### 1. Upgrade AtomicCopy to File.Replace (Q9 — 方案 A)

`AtomicCopy` now uses `File.Replace(source, destination, null, true)` — true cross-platform atomic swap:
- Windows: `ReplaceFile` Win32 API
- Unix: `rename(2)` (POSIX atomic)

temp path gets random suffix to prevent concurrent collision:
```csharp
var tempPath = destination + ".tmp." + Path.GetRandomFileName();
_copy(source, tempPath, true);
if (overwrite && File.Exists(destination))
    File.Replace(tempPath, destination, null, true);
else
    File.Move(tempPath, destination);
```

`File.Replace` requires source/temp/destination on the same volume — already guaranteed by tempPath = destination + ".tmp...".

### 2. Add async IFileOperations methods (Q10 — 方案 A)

Interface gains:
- `Task CopyAsync(string source, string destination, bool overwrite, CancellationToken ct)`
- `Task AtomicCopyAsync(string source, string destination, bool overwrite, CancellationToken ct)`
- `Task MoveAsync(string source, string destination, CancellationToken ct)`

`CopyAsync` uses `new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous)` + `CopyToAsync(dstStream, 65536, ct)` for true async I/O.
`AtomicCopyAsync` = `CopyAsync` to temp + `File.Move(temp, dest, overwrite:true)` (move is metadata-only, not blocking).
`MoveAsync` delegates to sync `File.Move` (move is metadata op, no async needed; wrapped in Task.CompletedTask for interface uniformity).

`FileTaskPipeline.HandleAsync` switches from `AtomicCopy` to `await AtomicCopyAsync(..., cancellationToken)`.

Sync methods retained for existing tests/mocks (LocalFileOperations constructor accepts Action<> delegates).

## Consequences

- Backup and modify paths on hot path are truly async — no thread-pool blocking
- `File.Replace` crash window eliminated — OS-atomic swap
- ~80 lines new async methods + ~20 lines pipeline call changes
- Existing sync interface unchanged → 0 test signature changes

## References

- pwm gpt56 research: File.Replace is cross-platform atomic (Windows ReplaceFile, Unix rename(2))
- MS docs: https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace
