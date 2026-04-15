# PhotoPrivacy Cleaner (MVP-1)

## Quick start
1. 复制 `config/config.sample.json` 为 `config/config.json` 并按实际环境修改。
2. 确认 `D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe` 和同级 `exiftool_files` 存在。
3. 运行 CLI（前台常驻）：

```bash
dotnet run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj
```

4. 运行 Windows Service（管理员 PowerShell）：

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

## Notes
- 当前实现严格采用 `FileSystemWatcher` 事件驱动，不轮询。
- ExifTool 设计目标是 `stay_open` 单进程桥接；MVP-1 当前骨架已具备命令构建与桥接测试。
- 请勿修改 ExifToolGUI 目录内容，本项目仅调用指定路径的 ExifTool 可执行文件。
