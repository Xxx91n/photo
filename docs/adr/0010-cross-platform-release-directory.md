# 跨平台发布目录结构统一到 release/<rid>/

弃用 publish/ 目录，所有编译输出统一到 release/<rid>/ 下。每个 RID 目录内为平铺结构：UI 可执行文件及所有依赖在根目录，Worker 二进制在 worker/ 子目录。用户下载 ZIP/安装包后直接得到直观的平铺结构。

## Considered Options

- **方案 A（采纳）**: release/<rid>/worker/ + 根下平铺 UI 及依赖
- **方案 B（否决）**: 沿用 publish/app/<version>/<rid>/ 层级结构——版本嵌套增加路径深度，不直观
