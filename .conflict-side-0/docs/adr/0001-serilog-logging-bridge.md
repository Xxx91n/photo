# Serilog 桥接 Microsoft.Extensions.Logging 作为统一日志框架

选择 Serilog + MS Extensions Logging 桥接方案，而非纯 MS Extensions Logging 自写 file provider。Serilog.Sinks.File 提供按天/按大小滚动 + retainedFileCountLimit 保留，Serilog.Sinks.Async 提供异步缓冲不阻塞 ExifTool 管线，Serilog.Settings.Configuration 通过 appsettings.json 驱动日志级别和 sink 配置（reloadOnChange 运行时切级别零代码改动）。现有 ILogger<T> 代码零改动。加 4 个 NuGet 包：Serilog、Serilog.Sinks.File、Serilog.Sinks.Async、Serilog.Settings.Configuration。

## Considered Options

- **纯 MS Extensions Logging + 自写 file provider**：零新依赖，但文件滚动、保留、异步缓冲全部需自己实现（200+ 行代码，容易出 bug），无法通过 appsettings.json 配置 sink。被否决。
