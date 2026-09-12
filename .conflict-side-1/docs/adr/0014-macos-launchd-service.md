# 新增 macOS launchd 服务安装脚本

新增 scripts/install-launchd-service.sh：生成 com.photoprivacy.cleaner.plist XML 配置，用 dscl 创建服务用户，launchctl load 加载服务。与 Windows install-service.ps1 和 Linux install-systemd-service.sh 形成三平台服务安装全覆盖。

## Considered Options

- **方案 A（采纳）**: 新增 launchd 脚本 plist XML + dscl 用户 + launchctl
- **方案 B（否决）**: 用 Homebrew service wrapper——引入不必要依赖
