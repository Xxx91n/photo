# PhotoPrivacy Cleaner

PhotoPrivacy 自动清除热文件夹中图片/视频/PDF 的隐私元数据（EXIF、IPTC、XMP 等），保护用户隐私。

## 架构

三进程拆分架构：

| 进程 | 职责 |
|------|------|
| `PhotoPrivacy.exe` | GUI 入口（Avalonia 桌面应用） |
| `PhotoPrivacyWorker.exe --mode background` | 后台 Worker（托盘/用户态） |
| `PhotoPrivacyWorker.exe --mode service` | 服务 Worker（Windows Service / Linux systemd） |

## 核心特性

### 文件监听
- **主监听**：`FileSystemWatcher` 事件驱动（inotify/ReadDirectoryChangesW）
- **降级兜底**：可选轮询补偿扫描器（`PollingFallbackScanner`），定期比对文件快照，补偿 FSW 漏事件
- **错误恢复**：FSW Error 事件自动重建 watcher + 强制轮询重扫

### ExifTool 桥接
- `stay_open` 单进程长驻协议，避免每文件启停开销
- Probe + Wipe 两阶段：先 `-json` 探测元数据，有隐私字段才执行 `-all=` 清除
- 支持连接池（`stay_open_pool_size`）并发处理

### 格式支持
- **109 种扩展名**：JPEG/TIFF/PNG/HEIF/AVIF/PSD/PDF/RAW/视频等
- 覆盖 ExifTool 98.2% 可写格式（仅 `.crm`、`.mie` 因极小众未纳入）

### Worker 健壮性
- **指数退避重试**：瞬态异常自动 1s→2s→4s→...→60s 退避，连续 10 次失败才停止宿主
- **优雅关停**：`TaskCanceledException` 不触发退避，直接进入 shutdown 路径
- **配置热重载**：通过 GUI 或 IPC 触发 `ReloadConfigAsync`，不停进程

### 跨平台诊断
- **Linux inotify 监控**：启动时读取 `/proc/sys/fs/inotify/max_user_watches`，>80% 使用率时告警
- **审计日志**：`diagnostic_mode=true` 时写 JSONL 审计（`_audit/audit-*.jsonl`）

## Quick Start

1. 复制 `config/config.sample.json` 为 `config/config.json` 并按环境修改。
2. 确认 ExifTool 路径可用：
   `C:\Program Files\ExifTool\exiftool.exe`
   且同级存在 `exiftool_files`。
3. 本地一次性验证（命令行单次执行）：

```bash
dotnet run --project src/PhotoPrivacy.Worker/PhotoPrivacy.Worker.csproj -- --mode cli --once true
```

4. 输出"最终生效配置"（不进入监听处理）：

```bash
dotnet run --project src/PhotoPrivacy.Worker/PhotoPrivacy.Worker.csproj -- --mode cli --config .\config\config.json --print-effective-config true
```

5. dry-run（不实际调用 ExifTool，只走流程并写审计）：

```bash
dotnet run --project src/PhotoPrivacy.Worker/PhotoPrivacy.Worker.csproj -- --mode cli --once true --dry-run true
```

## 运行模式（面向用户）

### 1) GUI 模式（推荐用户入口）

- 直接启动 `PhotoPrivacy.exe`
- GUI 通过 IPC 连接 Worker：
  - 若 Service Worker 在运行，GUI 进入"服务管理"状态
  - 否则连接/启动 Background Worker（托盘态）
- 默认会显示主窗口；如需静默启动可在 `config/config.json` 中设置 `ui.hide_main_window_on_startup=true`。
- GUI 中"保存更改"仅写入 `config/config.json`，不会立即重启运行态；点击"应用配置"后才按当前模式热生效。
- Service 模式与托盘模式共享同一份 `config/config.json`，应用配置时会按当前模式执行重连/重启链路。
- 主题支持 `ui.theme_variant=system|light|dark`，也可在 GUI 配置页直接切换。

### 2) Background Worker

```powershell
PhotoPrivacyWorker.exe --mode background --config .\config\config.json
```

注意：`PhotoPrivacyWorker.exe` 是后台 Worker，不提供 GUI。请使用 `PhotoPrivacy.exe` 作为桌面入口。

### 3) Service Worker

```powershell
PhotoPrivacyWorker.exe --mode service --config .\config\config.json
```

## Windows 服务安装

推荐使用脚本（管理员 PowerShell）：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-service.ps1 -ExePath .\publish\app\0.1.0-preview\win-x64\PhotoPrivacyWorker.exe -ConfigPath .\config\config.json
```

手工 `sc.exe` 示例：

```powershell
sc.exe create PhotoPrivacyCleaner binPath= "\"C:\path\PhotoPrivacyWorker.exe\" --mode service --config \"C:\path\config.json\"" start= auto
sc.exe start PhotoPrivacyCleaner
```

## Linux 部署（systemd）

1) 打包 Linux 发布物：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-app.ps1 -Version 0.1.0-preview -Runtime linux-x64 -SelfContained true -Zip true
```

2) 上传并解压 `publish/PhotoPrivacy-<version>-linux-x64.tar.gz` 到主机（推荐 `/opt/photoprivacy`）。

3) 安装并启动 systemd 服务（root）：

```bash
sudo bash scripts/install-systemd-service.sh --install-dir /opt/photoprivacy --config /opt/photoprivacy/config/config.json
```

## 配置说明

### `watch` 监听配置

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `hot_folder` | string | `D:\hot` | 监听目录 |
| `include_subdirectories` | bool | `true` | 递归监听子目录 |
| `debounce_ms` | int | `800` | 文件事件去抖毫秒 |
| `internal_buffer_size` | int | `65536` | FSW 内部缓冲区（字节） |
| `auto_excluded_directories` | string[] | `[]` | 自动排除的子目录 |
| `polling_interval_seconds` | int | `0` | 轮询补偿间隔（秒），0=禁用 |

### `exiftool` 配置

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `path` | string | (系统默认) | ExifTool 可执行文件路径 |
| `stay_open_pool_size` | int | `1` | ExifTool 连接池大小 |
| `max_parallel_drain` | int | `1` | 并行处理文件数 |
| `dry_run` | bool | `false` | 仅模拟，不实际调用 ExifTool |
| `enable_windows_long_path` | bool | `true` | 启用 Windows 长路径支持 |
| `enable_large_file_support` | bool | `true` | 启用大文件支持 |

### `retry` 配置

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `max_attempts` | int | `3` | 单文件最大重试次数 |
| `backoff_seconds` | int[] | `[1, 3, 10]` | 退避秒数序列 |

## Verification

```bash
dotnet test PhotoPrivacy.sln
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1 -HotFolder D:\hot -AuditFolder D:\hot\_audit
```

## 开发者附录（调试与诊断）

以下命令用于开发调试，普通用户可忽略：

```bash
dotnet run --project src/PhotoPrivacy.Worker/PhotoPrivacy.Worker.csproj -- --mode cli --once true
dotnet run --project src/PhotoPrivacy.Worker/PhotoPrivacy.Worker.csproj -- --mode cli --config .\config\config.json --print-effective-config true
dotnet run --project src/PhotoPrivacy.Worker/PhotoPrivacy.Worker.csproj -- --mode cli --once true --dry-run true
```

## Build EXE / Package

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-app.ps1 -Version 0.1.0-preview -Runtime win-x64 -Framework net10.0 -SelfContained true -Zip true
```

Linux:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-app.ps1 -Version 0.1.0-preview -Runtime linux-x64 -Framework net10.0 -SelfContained true -Zip true
```

发行前检查（测试 + smoke + 打包）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release-readiness.ps1 -Version 0.1.0-preview -Runtime win-x64
```

强杀回归（可选，验证父进程被强杀后 ExifTool 子进程不会残留）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-force-kill-cleanup.ps1 -Version 0.1.0-preview
```

## Notes

- 监听实现严格使用事件驱动（`FileSystemWatcher`/inotify），不使用轮询作为主监听方式。可选的 `PollingFallbackScanner` 仅作为降级兜底补偿 FSW 漏事件。
- ExifTool 使用 `stay_open` 单进程桥接。
- 不要修改 `ExifToolGUI` 目录任何文件，本项目仅读取并执行指定 ExifTool 路径。
