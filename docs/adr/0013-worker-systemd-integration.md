# Worker 添加 Systemd 集成 + 无条件 OutputType=Exe

Worker csproj 添加 Microsoft.Extensions.Hosting.Systemd NuGet 包。OutputType 改为无条件 Exe（移除条件判断）。Program.cs 按 OS 调用 UseSystemd()：Linux 下启动 systemd 日志集成，Windows 下用 WindowsServices 集成。

## Considered Options

- **方案 A（采纳）**: 加 Microsoft.Extensions.Hosting.Systemd + OutputType 改无条件 Exe
- **方案 B（否决）**: 保留条件 OutputType——已验证两个分支都是 Exe，条件冗余
