# PhotoPrivacy Cleaner (MVP-1)

## Quick start
1. 复制 `config/config.sample.json` 为 `config/config.json` 并按实际环境修改。
2. 确认 `D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe` 和同级 `exiftool_files` 存在。
3. 本地调试（CLI 模式，前台输出日志）：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --mode cli
```

4. 仅运行一次（适合测试）：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --mode cli --once true
```

5. 输出“最终生效配置”（不进入监听/处理流程）：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --mode cli --config .\config\config.json --print-effective-config true
```

6. 启用 dry-run（不实际调用 ExifTool，仅走流程并写审计）：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --mode cli --once true --dry-run true
```

## 运行模式（同一个 EXE）

发布后主程序名为 `PhotoPrivacy.exe`，支持三种模式：

1) 后台模式（默认）
- 双击 `PhotoPrivacy.exe` 即进入后台模式（托盘图标）
- 等价参数：

```powershell
PhotoPrivacy.exe --mode background
```

- 托盘右键菜单：`暂停` / `继续` / `退出`

2) 服务模式
- 直接运行：

```powershell
PhotoPrivacy.exe --mode service
```

- 推荐安装脚本（管理员 PowerShell）：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-service.ps1 -ExePath .\publish\cli\0.1.0-preview\win-x64\PhotoPrivacy.exe -ConfigPath .\config\config.json
```

- 或手工 `sc.exe`：

```powershell
sc.exe create PhotoPrivacyCleaner binPath= "\"C:\path\PhotoPrivacy.exe\" --mode service --config \"C:\path\config.json\"" start= auto
sc.exe start PhotoPrivacyCleaner
```

3) CLI 模式（仅调试/脚本）

```powershell
PhotoPrivacy.exe --mode cli --once true
```

## 旧服务宿主（独立项目，兼容保留）

```powershell
dotnet publish src/PhotoPrivacy.Service/PhotoPrivacy.Service.csproj -c Release -o .\publish\service
sc.exe create PhotoPrivacyCleaner binPath= "$(Resolve-Path .\publish\service\PhotoPrivacy.Service.exe)"
sc.exe start PhotoPrivacyCleaner
```

## Verification
```bash
dotnet test PhotoPrivacy.sln
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1 -HotFolder D:\hot -AuditFolder D:\hot\_audit
```

## Build EXE

1) 生成单文件 EXE（含 zip 包）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-cli-exe.ps1 -Version 0.1.0-preview -Runtime win-x64 -SelfContained true -Zip true
```

2) 发行前一键检查（测试 + smoke + 打包）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release-readiness.ps1 -Version 0.1.0-preview -Runtime win-x64
```

3) 成熟版发布时间规划见：`release-roadmap-2026-04-17.md`

### 允许你验证程序功能的方法

1) **最安全流程验证（推荐）**：dry-run + 固定输出目录
- 在 `config/config.json` 中设置：
  - `exiftool.dry_run = true`
  - `rules.output_mode = "fixed_directory"`
  - `rules.output_directory = "<你的输出目录>"`
- 执行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1 -HotFolder D:\hot -AuditFolder D:\hot\_audit -ConfigPath .\config\config.json
```

- 期望结果：
  - 输出目录出现复制后的文件
  - 审计日志出现 `exiftool_started`、`dry_run_wipe_skipped` 和 `file_processing_succeeded`

2) **真实清理验证（会调用 ExifTool）**
- 在 `config/config.json` 中设置 `exiftool.dry_run = false`
- 并确认 `exiftool.path` 指向真实可执行文件，否则程序会快速失败退出（不会进入清理流程）
- 准备测试文件后执行：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --mode cli --config .\config\config.json --once true
```

- 期望结果（在 ExifTool 路径可用时）：
  - 成功文件被清理元数据（按你的输出策略落地）
  - 失败文件按重试后进入隔离目录
  - 审计日志含 `exiftool_started`/`file_detected`/`file_processing_started`/`file_retry_scheduled`/`file_processing_succeeded`/`file_processing_failed`/`file_quarantined`

- 期望结果（在 ExifTool 路径不可用时）：
  - 进程直接报错退出（`exiftool.path not found`）
  - 不会生成清理成功审计
  - 退出码非 0

3) **持续监听验证**
- 前台常驻运行：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --mode cli --config .\config\config.json
```

- 向热文件夹持续投放文件，观察：
  - 防抖是否生效（不会重复风暴处理）
  - `fsw_error` 后是否出现 `fsw_recovered`

## Notes
- 当前实现严格采用 `FileSystemWatcher` 事件驱动，不轮询。
- ExifTool 采用 `stay_open` 单进程桥接；支持 `dry_run` 方便无损联调。
- ExifTool 健康检查采用双阈值：首次启动探测 3s、运行中心跳 500ms；重启事件会记录失败原因到审计 `data` 字段。
- 当 `audit.log_directory` 或 `quarantine.directory` 位于 `watch.hot_folder` 子目录（例如默认的 `D:\hot\_audit` / `D:\hot\_quarantine`）时，程序会自动从 FSW 监听流中排除这些目录，无需手工写入 `rules.excluded_patterns`。
- 启动时会在 `service_started` 事件的 `data["已自动排除的子目录列表"]` 中输出实际自动排除的目录，便于确认。
- 请勿修改 ExifToolGUI 目录内容，本项目仅调用指定路径的 ExifTool 可执行文件。
