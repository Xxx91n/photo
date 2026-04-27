# PhotoPrivacy Cleaner

`PhotoPrivacy` 采用三进程拆分架构：

- `PhotoPrivacy.exe`：纯 GUI 入口（用户双击启动）
- `PhotoPrivacyWorker.exe --mode background`：后台 Worker（托盘/用户态）
- `PhotoPrivacyWorker.exe --mode service`：服务 Worker（Windows Service / Linux systemd）

## Quick start

1. 复制 `config/config.sample.json` 为 `config/config.json` 并按环境修改。
2. 确认 ExifTool 路径可用：
   `D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe`
   且同级存在 `exiftool_files`。
3. 本地一次性验证（命令行单次执行）：

```bash
dotnet run --project src/PhotoPrivacy.Worker/PhotoPrivacy.Worker.csproj -- --mode cli --once true
```

4. 输出“最终生效配置”（不进入监听处理）：

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
  - 若 Service Worker 在运行，GUI 进入“服务管理”状态
  - 否则连接/启动 Background Worker（托盘态）
- 默认会显示主窗口；如需静默启动可在 `config/config.json` 中设置 `ui.hide_main_window_on_startup=true`。
- GUI 中“保存更改”仅写入 `config/config.json`，不会立即重启运行态；点击“应用配置”后才按当前模式热生效。
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

- 监听实现严格使用事件驱动（`FileSystemWatcher`/inotify），不使用轮询。
- ExifTool 使用 `stay_open` 单进程桥接。
- 不要修改 `ExifToolGUI` 目录任何文件，本项目仅读取并执行指定 ExifTool 路径。
