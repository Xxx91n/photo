# GitHub Actions 仅手动触发 + matrix 多平台 runner

CI/CD 使用 workflow_dispatch 手动触发，不在 push/PR 上自动运行。Matrix 按平台拆 runner：win-x64/x86/arm64→windows-latest，linux-x64→ubuntu-latest，linux-arm64→ubuntu-24.04-arm（原生 runner），osx-arm64→macos-latest，osx-x64→macos-13。workflow_dispatch inputs 包含 create_release 复选框门控 release job。

## Considered Options

- **方案 A（采纳）**: workflow_dispatch 手动触发 + matrix 拆 runner
- **方案 B（否决）**: push 触发——用户要求不自动运行
- **方案 C（否决）**: 单 runner + QEMU 交叉编译——arm64 用原生 runner 更快更稳
