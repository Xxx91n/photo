# Serilog PathMaskingDestructuringPolicy 统一脱敏

选择复用现有 PathMaskingPolicy + 新增 PathMaskingDestructuringPolicy : IDestructuringPolicy，注册到 Serilog 管道。此 policy 在 Serilog 序列化 FilePath/SourcePath/path 等命名属性时调用 PathMaskingPolicy.Mask 统一脱敏。Console sink 和 File sink 写出的都是脱敏后的值；journald/launchd stdout/Windows 文件全部受益，无需逐 sink 配置。零新增 NuGet 依赖（IDestructuringPolicy 是 Serilog 自带扩展点）。

## Considered Options

- **每处写日志手动调 PathMaskingPolicy.Mask**：散落、易漏。被否决。
- **引入 Serilog.Expressions 或第三方脱敏库**：新依赖，违反 Ponytail。被否决。
- **复用现有 Mask + DestructuringPolicy（当前选择）**：统一在管道层脱敏，零新依赖。

## Consequences

- 新增 PathMaskingDestructuringPolicy 类，注册到 Serilog LoggerConfiguration.Destructure.With()。
- 现有 JsonLineAuditLogger 的脱敏逻辑保持不变（业务审计和运行日志是不同场景）。
- 需测试验证含 FilePath 属性的日志消息在 Serilog 输出中已哈希脱敏。
