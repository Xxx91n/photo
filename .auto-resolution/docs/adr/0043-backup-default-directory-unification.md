# ADR 0043: Backup Default Directory Unification (bak vs _backup bug fix)

**Date**: 2026-08-13
**Status**: Accepted
**Related**: ADR 0041 (Atomic ops), ADR 0042 (Retention async)

## Context

`RuleEngine.ResolveBackupPath` (`src/PhotoPrivacy.Core/Rules/RuleEngine.cs:66`) defaulted backup directory to `Path.Combine(_config.Watch.HotFolder, "bak")`.

`MetadataCleanerWorker.cs:290` defaulted backup directory to `Path.Combine(config.Watch.HotFolder, "_backup")`.

**Effect**: backups were written to `{HotFolder}/bak/` but retention scanned `{HotFolder}/_backup/` — retention policy NEVER matched any actual backup file. Users could exceed the size limit with no enforcement.

## Decision

### 1. Unify default to {HotFolder}/bak (Q16 — 方案 A)

- Both call sites use the same default: `Path.Combine(hotFolder, "bak")`.
- Extract a shared helper to prevent future drift:
  ```csharp
  // in RuleEngine or a new BackupPathResolver
  public static string ResolveDefaultBackupDir(string hotFolder) =>
      Path.Combine(hotFolder, "bak");
  ```
- Both `RuleEngine.ResolveBackupPath` and `MetadataCleanerWorker` call this helper.

### 2. Auto-inject into AutoExcludedDirectories (Q16 — 方案 A)

- On Worker startup (`MetadataCleanerWorker.Initialize`), if `Backup.Enabled && string.IsNullOrWhiteSpace(Backup.Directory)`, compute effective backup dir and add to `_autoExcludedSubdirectories`.
- Prevents FSW from watching its own backup directory (circular reprocessing).
- The UI's `SystemAutoExcludedDirectories` list will display this auto-injected entry as read-only.

## Consequences

- Retention actually scans the directory where backups live — policy enforcement restored
- FSW no longer sees `.bak` file creation in its own backup dir (no self-retrigger)
- User-visible in the "system auto-excluded" UI list, no surprise

## Verification plan

- Unit test: `RuleEngine.ResolveBackupPath` and `MetadataCleanerWorker` compute identical default dirs
- Unit test: effective backup dir is in `_autoExcludedSubdirectories` when Backup.Directory is empty
