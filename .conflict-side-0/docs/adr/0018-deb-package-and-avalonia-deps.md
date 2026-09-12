# Linux .deb 打包 + Avalonia native 依赖声明

CI Linux job 用 dpkg-deb --build 手动打 .deb（无新依赖）。.deb control 文件 Depends 声明 Avalonia native 依赖：libx11-6, libice6, libsm6, libfontconfig1, ca-certificates, tzdata, libc6, libgcc-s1, libstdc++6, zlib1g, libssl3, libicu。架构映射：linux-x64→amd64，linux-arm64→arm64，不做 armhf。

csproj 不声明 RuntimeIdentifiers，靠脚本 -r 传参（沿用 MPF/SabreTools 惯例）。macOS .app bundle 用 ditto + Info.plist 打包。不打包 ExifTool（用户自行下载，与 ExifToolGUI 一致）。

## Considered Options

- **方案 A（采纳）**: dpkg-deb --build + Avalonia Depends 声明
- **方案 B（否决）**: 用 Velopack 打包——引入重依赖，本项目不需要自动更新

## Consequences

- 用户需自行下载 ExifTool 并配置 config.json 路径
- .deb 安装时 apt 自动解析 Avalonia native 依赖
- macOS notarization 可选（用户自建不需，分发给他人时建议）
