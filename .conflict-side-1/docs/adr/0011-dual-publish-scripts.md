# 双发布脚本 publish.sh + publish.ps1 跨平台友好

本地发布脚本拆为双脚本：publish.ps1（Windows PowerShell）和 publish.sh（Linux/macOS Bash）。两脚本共用同一套 RID 矩阵逻辑，确保跨平台一致性。CI/CD 调用对应平台的脚本。

## Considered Options

- **方案 A（采纳）**: 双脚本 publish.sh + publish.ps1（行业惯例）
- **方案 B（否决）**: 单脚本用 PowerShell Core——Linux 默认不装 pwsh，不友好
- **方案 C（否决）**: 单脚本用 Python——引入不必要依赖
