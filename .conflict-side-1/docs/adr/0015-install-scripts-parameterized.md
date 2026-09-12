# 三平台安装脚本统一参数化 Worker binary 路径

install-service.ps1、install-systemd-service.sh、install-launchd-service.sh 统一参数化 Worker binary 路径，默认指向 release/<rid>/worker/PhotoPrivacyWorker。不再硬编码 publish/app/0.1.0-preview/ 版本路径。

## Considered Options

- **方案 A（采纳）**: 三脚本统一参数化路径，默认 release/<rid>/worker/
- **方案 B（否决）**: 保留硬编码版本路径——版本变更需改脚本
