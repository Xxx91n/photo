# 未处理异常日志统一到 Serilog（不写 minidump）

Worker Program.cs 新增 AppDomain.CurrentDomain.UnhandledException → Log.Fatal + TaskScheduler.UnobservedTaskException → Log.Error + e.SetObserved()。注册在 Log.Logger 初始化之后、app.RunAsync() 之前。Handler 末尾调 Log.CloseAndFlush() 保证 crash 前落盘。不写 minidump（跨平台 PInvoke 复杂，违反 Ponytail）。UI 进程同样注册但 Error 级别即可。

## Considered Options

- **写 minidump（Windows MiniDumpWriteDump + Linux core dump）**：跨平台 PInvoke 复杂，过度工程。被否决。
- **不处理 unhandled exception**：crash 信息丢失。被否决。
- **统一到 Serilog（当前选择）**：零新增依赖，保证 crash 信息落盘。

## Consequences

- Worker Program.cs 和 UI Program.cs 新增两个 handler 注册。
- 需测试模拟未处理异常并验证日志写入。
