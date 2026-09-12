# systemd RuntimeDirectory + macOS GUI 用户家目录 socket 路径

Linux systemd unit 加 RuntimeDirectory=photoprivacy（systemd 自动创建 /run/photoprivacy 并 chown 给 User= 指定用户）。Linux Client socket 路径=/run/photoprivacy/worker.sock（共享可读）。macOS socket 路径=~/Library/Application Support/photoprivacy/worker.sock（无需 root，GUI 用户可访问）。Windows 不涉及。

## Considered Options

- **/var/run 预创建脚本**：与 systemd RuntimeDirectory 重复。被否决。
- **/tmp 路径**：多用户可竞争，不安全。被否决。
- **RuntimeDirectory + 用户家目录（当前选择）**：systemd 原生支持，零新增脚本。

## Consequences

- install-systemd-service.sh unit 文件加 RuntimeDirectory=photoprivacy。
- 二次确认：Linux socket 位于 /run/photoprivacy/（RuntimeDirectory 创建），macOS 位于 ~/Library/Application Support/photoprivacy/（runtime 创建）。
- 需测试验证 systemd 启动后目录权限正确。
