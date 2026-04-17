# PhotoPrivacy Cleaner (MVP-1)

## Quick start
1. 复制 `config/config.sample.json` 为 `config/config.json` 并按实际环境修改。
2. 确认 `D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe` 和同级 `exiftool_files` 存在。
3. 运行 CLI（前台常驻）：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj
```

4. 仅运行一次（适合测试）：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --once true
```

7. 输出“最终生效配置”（不进入监听/处理流程）：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --config .\config\config.json --print-effective-config true
```

5. 启用 dry-run（不实际调用 ExifTool，仅走流程并写审计）：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --once true --dry-run true
```

6. 运行 Windows Service（管理员 PowerShell）：

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
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --config .\config\config.json --once true
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
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --config .\config\config.json
```

- 向热文件夹持续投放文件，观察：
  - 防抖是否生效（不会重复风暴处理）
  - `fsw_error` 后是否出现 `fsw_recovered`

## Notes
- 当前实现严格采用 `FileSystemWatcher` 事件驱动，不轮询。
- ExifTool 采用 `stay_open` 单进程桥接；支持 `dry_run` 方便无损联调。
- 请勿修改 ExifToolGUI 目录内容，本项目仅调用指定路径的 ExifTool 可执行文件。
