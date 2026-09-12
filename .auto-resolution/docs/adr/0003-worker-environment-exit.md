# Worker 致命异常路径调用 Environment.Exit(1)

在配置验证失败和连续失败超阈值两个路径，_applicationLifetime.StopApplication() 之后加 Environment.Exit(1)。微软文档明确要求 non-zero exit code 才能触发 Windows SCM 恢复策略（如 sc.exe failure ... actions= restart/60000）。StopApplication() 走优雅关闭路径，进程正常退出，SCM 不重启——zombie service 风险。Environment.Exit(1) 是确定性修复。

## Considered Options

- **仅依赖 BackgroundServiceExceptionBehavior.StopHost**：让异常从 ExecuteAsync 泄漏，host 自动停止。但进程正常退出，SCM 不重启。被否决。
