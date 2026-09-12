# ADR 0037: Instant-Apply Config + Audit Log Clear-to-Now

**Date**: 2026-08-13
**Status**: Accepted
**Related**: ADR 0001 (Serilog), ADR 0036 (Service fixes)

## Context

Two UX/privacy bugs:

1. **Config changes not persisted on restart**: The UI had a manual "应用配置" button.
   Users who changed settings (toggles, paths) and restarted without clicking it
   got their changes silently lost — the ViewModel held values in memory but
   `ConfigEditor.UpdateConfig` was only called on button click. This is especially
   non-obvious for `ToggleSwitch` changes which feel "applied" already.

2. **Log clear is fake**: `ClearLogs()` only cleared the in-memory `ObservableCollection`.
   `AuditTailService._lastPosition` was NOT reset, so the next FSW poll re-read
   and re-appeared old entries. On restart, `TryReadNewLines()` read from position 0
   — the entire audit file history re-appeared. For a privacy tool, this is unacceptable:
   the user believed they cleared sensitive activity history.

## Decision

### 1. Instant-apply via debounced PropertyChanged (Q1 — 方案 A)

- Removed the "应用配置" button entirely.
- `MainWindow.axaml.cs` subscribes to `viewModel.PropertyChanged`.
- A `_configProperties` HashSet filters which property changes trigger the debounce
  (only configurable settings, not runtime status like `RuntimeStatus`).
- A 500ms debounce timer (`_configApplyDebounceCts`) fires `ApplyConfigImmediatelyAsync`
  which calls the existing `ConfigEditor.UpdateConfig` → atomic write + Worker IPC reload.
- Result: any setting change auto-saves within 500ms of the user's last interaction.
  No manual action needed. `SaveStatus` shows "✓ 已自动保存" for 3s.

### 2. AuditTailService starts at file end (Q3 — 方案 A)

- `Start()` now calls `SkipToCurrentEnd()` instead of `TryReadNewLines()`.
- `SkipToCurrentEnd()` sets `_lastPosition = stream.Length` so only NEW entries
  (written after this UI launch) appear in the log page.
- On restart: no history replay. Clean slate.

### 3. Clear logs = skip to current end (Q2 — 方案 B)

- `OnClearLogsClick` now calls `_auditTail?.SkipToCurrentEnd()` after `vm.ClearLogs()`.
- Both memory and read-position are reset to "now".
- The disk audit file is NOT modified — the Worker's audit record remains intact
  for compliance, but the UI presents a clean slate.
- On restart: still clean (because of Q3 `SkipToCurrentEnd` on start).

### Removed dead code

- `SetConfigButtonsBusy()` — the only purpose was toggling the removed button.
- `ApplyConfigButton` control reference in AXAML.
- `OnApplyConfigClick` handler logic (replaced by `ApplyConfigImmediatelyAsync`).

## Verification

- Build: 0 errors
- Tests: 244/244 pass (114 Core + 127 Integration + 3 ReleaseReadiness)
- Publish win-x64: Worker 73.1MB single-file, config.json present
- Runtime: UI starts, MainWindowHandle visible, no crash, no deadlock
