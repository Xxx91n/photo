# UiDiagnosticLog 统一到 Serilog 管道

UiDiagnosticLog.Write(msg) 内部改为 Log.Information(msg) 委托，调用点零改动。Serilog File sink 自动处理滚动（按天 + 按大小）和保留，不再有无限增长的单文件。所有日志统一管道、统一格式、统一保留策略。

## Considered Options

- **保留独立但加滚动**：UiDiagnosticLog 不动 Serilog，自己加文件大小上限 + 滚动（约 30 行代码）。两套日志逻辑，维护成本翻倍。被否决。
