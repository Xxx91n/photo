# PhotoPrivacy Cleaner

PhotoPrivacy 自动清除热文件夹中图片/视频/PDF 的隐私元数据（EXIF、IPTC、XMP 等），保护用户隐私。

## 架构

三进程拆分架构：

| 进程 | 职责 |
|------|------|
| `PhotoPrivacy.exe` | GUI 入口（gpui 桌面应用，Rust，构建产物为 photoprivacy-ui，源码在 `ui/`） |
| `PhotoPrivacyWorker.exe --mode background` | 后台 Worker（托盘/用户态） |
| `PhotoPrivacyWorker.exe --mode service` | 服务 Worker（Windows Service / Linux systemd） |

## 核心特性

### 文件监听
- **主监听**：`FileSystemWatcher` 事件驱动（inotify/ReadDirectoryChangesW）
- **漏事件保底**：可选轮询补偿扫描器（`PollingFallbackScanner`），定期比对文件快照，补偿 FSW 漏事件
- **错误恢复**：FSW Error 事件自动重建 watcher + 强制轮询重扫

### ExifTool 桥接
- `stay_open` 单进程长驻协议，避免每文件启停开销
- Probe + Wipe 两阶段：先 `-json` 探测元数据，有隐私字段才执行 `-all=` 清除
- 支持连接池（`stay_open_pool_size`）并发处理
- **随包内置**：发布包在 `<包根>/ExifTool/` 带一份固定版本（Windows 为自带 Perl 运行时的 `exiftool.exe` + `exiftool_files/`；Linux/macOS 为 `exiftool` 脚本 + `lib/`，需目标机有 `perl`）。配置模板的 `exiftool.path` 留空即按运行时探测：内置目录优先，其次系统安装位置
- **自带 ExifTool（#33）**：`exiftool.path` 也可直接填一个**安装目录**（对齐 ExifToolGUI 的 `ETOverrideDir` 做法），运行时解析出目录内的 `exiftool.exe` / `exiftool`；构建发版时用 `--exiftool-path-override <目录>`（`publish.sh`）或 `-ExifToolPathOverride <目录>`（`publish-app.ps1`）直接打包该目录、跳过下载。发布脚本钉死的 SHA256 由 `scripts/verify-exiftool-download.sh` 与官方 `exiftool.org/checksums.txt` 核对（CI 在发布脚本变更时触发，另做周常巡检）

### 格式支持
- **37 种扩展名**（与擦除策略映射表一一对应）：JPEG（jpg/jpeg/jpe/jps/jph）、TIFF/DNG、RAW（cr2/cr3/arw/nef/orf/raf/rw2/pef/srw/sr2）、PNG/APNG/WebP/GIF/PSD、HEIF/AVIF、MOV/MP4（mov/mp4/m4v/qt/3gp/3g2）、PDF、EPS/PS/AI
- **不支持格式明确跳过**：映射面之外的格式（如长尾 RAW 变体）不做任何清理，立即跳过并写入 `wipe_skipped_unknown` 审计事件（fail-closed，绝不静默处理）
- **GIF 属部分清理**：注释 / XMP / ICC 能删；C2PA（JUMBF）、Plain Text、MIDI 块 ExifTool 13.61 删不掉（静默保留，审计不谎报已删）。依据：docs/research/exiftool-jxl-webp-psd-gif-apng.md §3.3
- **PSD 属部分清理**：EXIF / XMP / IPTC / ICC（IRB 内）能删；版权标记、URL、分辨率、打印参数、缩略图（0x040C）以及图层内容、图层名、图层修改时间删不掉。依据：docs/research/exiftool-jxl-webp-psd-gif-apng.md §3.4（含 42 图层真实样本实测）
- **视频属部分清理**（mov/mp4/m4v/qt/3gp/3g2）：用真样本实测过（ExifTool 13.61，命令 `-All= -Time:All=`）——iPhone 录像的 GPS、设备型号、软件名、创建时间全删，C2PA（JUMBF）官方测试样本也删干净，画面数据字节不变、全量解码通过；编码器指纹还在，官方说法本来就是「删掉大部分顶层元数据」。默认规则下，拍摄日期不删，只把值改成 0，少数播放器会显示 1904 年或空白，播放不受影响。**分片 MP4（moof，录像笔 / DASH 常见）擦不掉**：ExifTool 13.61 会报错、文件一个字节不动；产品的常驻通道看不出这条错误，按代码路径会把文件记成已清理（这一条没跑端到端实测）。别直接处理这类文件，先转成普通 MP4，详见 docs/research/exiftool-mp4-mov-m4v-3gp.md §12.3。带 EXIF 结构的视频删除路径仍没样本可测（这五个真实设备样本用 family 0 复核也没读到 EXIF 结构）；样本、哈希和逐项数字见 docs/research/exiftool-mp4-mov-m4v-3gp.md §12
- 数字由 CI 断言钉死：`allowed_extensions` ⊆ `ExtensionFamilyMap` 且集合差 = 0（含本行数字），宣称与实现脱钩就 CI 失败

### Worker 健壮性
- **指数退避重试**：瞬态异常自动 1s→2s→4s→...→60s 退避，连续 10 次失败才停止宿主
- **优雅关停**：`TaskCanceledException` 不触发退避，直接进入 shutdown 路径
- **配置热重载**：通过 GUI 或 IPC 触发 `ReloadConfigAsync`，不停进程

### 跨平台诊断
- **Linux inotify 监控**：启动时读取 `/proc/sys/fs/inotify/max_user_watches`，>80% 使用率时告警
- **审计日志**：`diagnostic_mode=true` 时写 JSONL 审计（`_audit/audit-*.jsonl`）

## Quick Start

1. 复制 `config/config.sample.json` 为 `config/config.json` 并按环境修改。
2. 确认 ExifTool 可用：发布包自带 `<包根>/ExifTool/`，不必另装；自行安装时把 `exiftool.path` 指到可执行文件
   或指向其安装目录（#33，目录内需有 `exiftool.exe` + `exiftool_files`，或 `exiftool` + `lib/`），
   也可放在 `/usr/bin`、`/usr/local/bin` 等系统目录由运行时探测。
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
powershell -ExecutionPolicy Bypass -File .\scripts\install-service.ps1 -ExePath .\release\win-x64\worker\PhotoPrivacyWorker.exe -ConfigPath .\config\config.json
```

手工 `sc.exe` 示例：

```powershell
sc.exe create PhotoPrivacyCleaner binPath= "\"C:\path\PhotoPrivacyWorker.exe\" --mode service --config \"C:\path\config.json\"" start= auto
sc.exe start PhotoPrivacyCleaner
```

## Linux 部署（systemd）

1) 在 Linux x86_64 主机上打包 Linux 发布物（gpui 新 UI 不支持交叉编译，发布脚本须在目标平台本机执行）：

```bash
./scripts/publish.sh --version 0.1.0-preview --runtime linux-x64 --zip true
```

2) 上传并解压 `release/PhotoPrivacy-<version>-linux-x64.tar.gz` 到主机（推荐 `/opt/photoprivacy`）。

3) 安装并启动 systemd 服务（root）：

```bash
sudo bash scripts/install-systemd-service.sh --install-dir /opt/photoprivacy --config /opt/photoprivacy/config/config.json
```

## 配置说明

发布模板默认将 `watch.hot_folder` 留空。Worker 会保持 IPC 连接并进入待机状态，UI 会提示前往配置页选择监控目录；保存有效目录后通过配置热重载启动监控，无需重启 Worker。发布包中的 `config/README.md` 也包含这段首次配置说明。

### `watch` 监听配置

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `hot_folder` | string | `""` | 监听目录；留空时 Worker 待机并等待配置 |
| `include_subdirectories` | bool | `true` | 递归监听子目录 |
| `debounce_ms` | int | `800` | 文件事件去抖毫秒 |
| `internal_buffer_size` | int | `65536` | FSW 内部缓冲区（字节） |
| `auto_excluded_directories` | string[] | `[]` | 自动排除的子目录 |
| `polling_interval_seconds` | int | `0` | 轮询补偿间隔（秒），0=禁用 |

### `exiftool` 配置

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `path` | string | `""` | ExifTool 可执行文件路径；留空 = 运行时探测（发布包内置 `ExifTool/` 优先，其次系统安装位置） |
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

> **发布前必须先测试**：发布脚本内含 `dotnet publish`（Worker）与 `cargo build`（GUI 入口）但跳过完整 test 验证。
> 推荐使用 `release-readiness.ps1`（含 test + smoke + publish 的完整检查）代替直接 publish。
> 直接 publish 而不先 test 会导致 bug 已修但发布的二进制仍是旧版。

Windows（GUI 入口走 cargo 的 windows-gnu target，需 MinGW `windres` 在 PATH 或 `WINDRES` 环境变量）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-app.ps1 -Version 0.1.0-preview -Runtime win-x64 -Framework net10.0 -SelfContained true -Zip true
```

Linux/macOS（gpui 新 UI 不支持交叉编译，须在目标平台本机执行）：

```bash
./scripts/publish.sh --version 0.1.0-preview --runtime linux-x64 --zip true   # 亦支持 linux-arm64 / osx-x64 / osx-arm64
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

- 监听实现严格使用事件驱动（`FileSystemWatcher`/inotify），不使用轮询作为主监听方式。可选的 `PollingFallbackScanner` 仅作为漏事件时的保底补偿。
- ExifTool 使用 `stay_open` 单进程桥接。
- 不要修改 `ExifToolGUI` 目录任何文件，本项目仅读取并执行指定 ExifTool 路径。
