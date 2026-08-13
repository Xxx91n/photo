# ADR 0036: Service Mode File Logging + Mutex Per Mode + config.json Release + install-service.ps1 Quote Fix

**Date**: 2026-08-13
**Status**: Accepted
**Related**: ADR 0026 (ISingleInstanceGuard), ADR 0035 (Publish Script Properties)

## Context

Windows service `PhotoPrivacyCleaner` failed to start — clicking "Start" produced no visible result
(Win32 error 1053: "服务没有及时响应启动或控制请求"). Root cause analysis revealed three
compounding bugs:

1. **config.json missing from release**: `publish-app.ps1` and `publish.sh` only copied
   `config.sample.json` to the release directory. The service binPath referenced
   `config\config.json` which did not exist. Worker started → FileNotFound → silent exit.

2. **Service mode had NO file logging**: `Program.cs:55` had `if (mode != RuntimeMode.Service)`
   guarding the Serilog file sink. In service mode, only Console output was configured — but SCM
   does not read stdout, so crashes were completely invisible. This is why the failure presented
   as "clicking start has no reaction."

3. **Background and Service shared the same Mutex**: `WorkerInstanceMutexNames` mapped both
   `Background` and `Service` to `Global\PhotoPrivacyWorker_Instance`. When the UI launched
   a background-mode Worker (holding the Mutex), the Windows service could not start — the
   service process saw "另一个 Worker 实例已在运行" and exited immediately.

4. **install-service.ps1 binPath construction used triple-quote escaping** (`"""+$ExePath+"``)
   that broke PowerShell 5.1 parsing with `ParserError: Unexpected token 'mode'`.
   Additionally, `$ExePath` and `$ConfigPath` had no auto-detection defaults, requiring the
   user to pass them manually.

## Decision

### 1. File logging in ALL modes (including Service)

Removed the `if (mode != RuntimeMode.Service)` guard. The Serilog Async File sink is now
configured unconditionally. Service mode writes to `logs/worker-.log` just like background/
CLI modes, with the same rolling/retention policy. This ensures service crashes are never
invisible again.

### 2. Separate Mutex per runtime mode

```csharp
public const string Background = @"Global\PhotoPrivacyWorker_Background";
public const string Service    = @"Global\PhotoPrivacyWorker_Service";
```

`Program.cs` selects the mutex name based on `requestedMode`. Background and Service are
distinct runtime modes with separate IPC endpoints (`PhotoPrivacyCleaner.Background` vs
`PhotoPrivacyCleaner.Service`) — they must not block each other. The CLI mode falls under
the Background mutex (same process type as UI-launched worker).

### 3. config.json in release

Both `publish-app.ps1` and `publish.sh` now copy `config.sample.json` as both
`config.sample.json` AND `config.json` to the release `config/` directory. The release
has a working default config out of the box.

### 4. install-service.ps1: auto-detect defaults + fixed quote escaping

- Auto-detects `$ExePath` (`<script>/../PhotoPrivacyWorker.exe`) and `$ConfigPath`
  (`<script>/../config/config.json`) relative to the script location.
- Validates both paths exist before proceeding.
- binPath construction fixed with `-f` format operator:
  ```powershell
  $binPath = '"{0}" --mode service --config "{1}"' -f $ExePath, $ConfigPath
  ```

## Verification

- Build: 0 errors (701 pre-existing CA warnings)
- Tests: 244/244 pass (114 Core + 127 Integration + 3 ReleaseReadiness)
- Publish win-x64: Worker 73.1MB (single-file), config.json present in release
- Runtime: Service starts → State=RUNNING, PID alive, logs written to `logs/worker-.log`
- Runtime: UI (service mode) → MainWindowHandle visible, no deadlock, connects to service IPC endpoint
