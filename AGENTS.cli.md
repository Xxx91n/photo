# AGENTS.cli.md - PhotoPrivacy CLI Instruction Set

> This file defines the Worker CLI mode (`--mode=cli`) instruction set, GUI/CLI feature alignment rules, and IPC protocol constraints.
> Agents must read this file before modifying CLI-related code.

---

## 1. Runtime Modes

| Mode | Launch arg | Purpose |
|------|-----------|---------|
| `Background` | `--mode=background` (default interactive) | UI starts Worker subprocess, same-user IPC |
| `Service` | `--mode=service` | System service (Windows SCM / systemd / launchd) |
| `Cli` | `--mode=cli` | Command-line single execution, no UI, no IPC server |

`RuntimeModeResolver.ResolveFromArgs(args)` parses `--mode=<value>` or `--mode <value>`.

---

## 2. CLI Instruction Set (IPC Methods)

CLI mode does not start IPC server, but can send commands to a running Background/Service instance via `WorkerIpcClient`:

| Method | Description | CLI Available |
|--------|-------------|---------------|
| `Ping` | Heartbeat probe, returns `ok: true` | Yes |
| `GetStatus` | Get Worker status (IsPaused / ExifToolVersion / WatchDirectory / Mode) | Yes |
| `Pause` | Pause file processing pipeline | Yes |
| `Resume` | Resume file processing pipeline | Yes |
| `GetExifToolVersion` | Get ExifTool version string | Yes |
| `ReloadConfig` | Hot-reload config file (same path as SIGHUP) | Yes |
| `Shutdown` | Request Worker stop (CLI/Service maintenance context only) | Restricted - not exposed to Background |

Protocol version field: `WorkerIpcRequest.V` (optional, default 1), `WorkerIpcResponse.V` corresponding return.

---

## 3. GUI/CLI Feature Alignment

| Feature | GUI Path | CLI/IPC Path | Aligned |
|---------|----------|--------------|---------|
| Pause/Resume | `MainWindowViewModel.PauseResumeCommand` | `Pause` / `Resume` IPC | Yes |
| Status Query | `ConnectionStateService` heartbeat | `GetStatus` IPC | Yes |
| ExifTool Version | `MainWindowViewModel.ExifToolVersion` | `GetExifToolVersion` IPC | Yes |
| Config Hot Reload | `ConfigEditor.UpdateConfig` + debounced write | `ReloadConfig` IPC / SIGHUP | Yes |
| Shutdown | `WorkerProcessManager.ShutdownWorker` | `Shutdown` IPC (CLI/Service only) | Yes |

---

## 4. CLI Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| `--mode=<background|service|cli>` | Runtime mode | `background` (interactive without args) |
| `--config=<path>` | Config file path | `<baseDir>/config/config.json` |
| `--hot-folder=<path>` | Override hot_folder config | Config file value |
| `--print-effective-config` | Print effective config then exit | - |

---

## 5. Security Constraints

- Shell injection: ExifTool `stay_open` mode, `ValidatePathForExifToolProtocol` rejects newlines, `-` prefix, null bytes, `..` traversal
- Path validation: `WatchPathFilter.IsSubPathRelative` rejects `..` relative paths
- IPC message limit: 64 KB (`MaxMessageBytes` / `MaxResponseBytes`)
- Config paths: no hardcoded developer-private paths, cross-platform defaults via `DefaultPaths`

---

## 6. Prohibitions (CLI-specific)

| Prohibited | Reason |
|------------|--------|
| CLI mode exposes `Shutdown` to Background IPC | ADR 0031: Background does not accept external shutdown |
| `Process.Start` with `UseShellExecute = true` | Command injection risk |
| Hardcoded `D:\hot` or any developer-private path | Cross-platform portability |
| CLI params used directly for file paths without validation | Path traversal |

---

## 7. References

- ADR 0031: Mode-scoped shutdown
- ADR 0033: Config reload validation
- ADR 0030: IPC protocol version field
- `src/PhotoPrivacy.Ipc/WorkerIpcContracts.cs` - IPC method constants
- `src/PhotoPrivacy.Worker/RuntimeModeResolver.cs` - Mode resolution
