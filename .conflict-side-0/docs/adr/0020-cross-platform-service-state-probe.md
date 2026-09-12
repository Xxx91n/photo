# 跨平台服务状态探测抽象（IServiceStateProbe 三平台实现）

选择抽象 IServiceStateProbe 接口 + 三平台实现：Windows 现有 ServiceController/sc.exe 保持，Linux 新增 SystemdStateProbe（systemctl is-active/is-enabled 映射），macOS 新增 LaunchdStateProbe（launchctl list 解析 PID + 退出码）。工厂按 OS 切换实现，UI 层零改动（已通过接口注入）。

## Considered Options

- **UI 层硬编码 if/else 分支**：散落 ViewModel 中 OperatingSystem.IsWindows() 判断，违反抽象。被否决。
- **MQTT/REST 健康端口**：过度工程化，新增网络栈依赖。被否决。
- **抽象接口 + 三实现（当前选择）**：UI 已通过 IServiceStateProbe 注入，零改动，按 OS 切换实现。

## Consequences

- 新增 SystemdStateProbe、LaunchdStateProbe 两个类 + 工厂方法。
- ServiceRuntimeState 枚举复用现有值（Running/Stopped/NotInstalled/Unknown），systemctl/launchctl 输出映射到这些值。
- 新增三平台各自的单元测试（用假 Process 输出验证解析逻辑）。
