# Serilog 条件化文件 sink（systemd 用 journald + macOS 靠 Serilog 滚动）

选择 Serilog 条件化文件 sink：WriteTo.Console 无条件（systemd 进 journald，macOS 进 launchd stdout 文件），WriteTo.File 改 Conditional 模式仅当运行模式非 Service 时启用（桌面 Worker+CLI 需要文件日志；systemd/launchd 后台模式不需要因为 console 已进 journald/launchd 文件）。Linux install 脚本内置 logrotate 配置模板放 /etc/logrotate.d/photoprivacy（覆盖 Serilog 文件 sink 在桌面模式下的轮转），macOS 靠 Serilog 文件 sink 自身滚动。

## Considered Options

- **全部平台都写文件 sink + logrotate**：重复（console 和文件双写），增加运维复杂度。被否决。
- **引入 Loki 等集中日志系统**：过度工程化。被否决。
- **条件化文件 sink（当前选择）**：service 模式靠 journald/launchd 捕获 console；桌面模式靠 Serilog 文件 sink 滚动。零新增依赖。

## Consequences

- appsettings.json 的 WriteTo.File 条目加 Conditional 过滤（基于运行模式或环境变量）。
- install-systemd-service.sh 新增 logrotate 配置片段（桌面模式文件日志轮转兜底）。
- macOS launchd 靠 Serilog rollingInterval + retainedFileCountLimit 覆盖。
