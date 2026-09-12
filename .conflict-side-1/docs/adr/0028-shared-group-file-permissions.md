# 共享组 + setgid 目录 + GUI 用 IPC 读

Linux/macOS install 脚本在创建 photoprivacy 用户+组之外，提示把交互用户加入 photoprivacy 组（文档化，不强制）。hot-folder、audit、quarantine 目录 chmod 2770（setgid，组成员可读写、新建子目录继承组）。GUI 不直接访问 quarantine（敏感），统一通过 IPC 请求服务读审计日志/导出数据（复用第一轮 DataExportService）。GUI 只需审计日志查看权限（用户加入共享组后即可），quarantine 不开放给 GUI 直接。Windows 不涉及（LocalSystem 与 GUI 同账户场景已无此问题）。

## Considered Options

- **全部文件通过 IPC 代理**：最严格但 IPC 接口量大增，违反 Ponytail。被否决。
- **服务用交互用户身份运行**：破坏最小权限原则。被否决。
- **共享组 + setgid + IPC 代理 quarantine（当前选择）**：兼顾最小权限与实现成本。

## Consequences

- install-systemd-service.sh / install-launchd-service.sh 的 chmod 改为 2770。
- README/AGENTS.md 文档「加入 photoprivacy 组」步骤。
- DataExportService 已支持（第一轮 ADR 0008），GUI 通过 IPC 调用即可。
