# 配置说明

发布模板会有意将 `watch.hot_folder` 留空。Worker 会保持 IPC 连接并进入待机状态，UI 会提示你前往配置页选择监控目录。选择有效目录并保存配置后，Worker 会通过热重载启动监控，无需重启。

## `exiftool.path` 留空的含义

模板里 `exiftool.path` 为空串，表示**按运行时探测**：

1. 发布包内置的 `<包根>/ExifTool/`（Windows 为 `exiftool.exe` + `exiftool_files/`；Linux/macOS 为 `exiftool` + `lib/`，需系统有 `perl`）；
2. 系统安装位置（Windows 的 `C:\Program Files\ExifTool\` 等；Linux/macOS 的 `/usr/bin`、`/usr/local/bin`、`/opt/homebrew/bin` 等 PATH 可见位置）。

手填的绝对路径始终优先，不会被探测覆盖。内置的 ExifTool 被删或被安全软件隔离时，保存监控目录会以可读原因失败（提示指向重新解压发布包或安装 ExifTool），该提示常驻显示，不会几秒后消失。

## `exiftool.path` 指向安装目录（#33）

除填可执行文件的完整路径外，也可以填一个**目录**（对齐 ExifToolGUI 的 `ETOverrideDir` 做法）：Worker 加载配置时会解析出该目录内的 `exiftool.exe`（Windows）或 `exiftool`（Linux/macOS），再按同一套规则校验伴随的 `exiftool_files/` 或 `lib/`。因此「自带一份官方版 ExifTool」不需要把路径写到文件级，指向解压后的目录即可；目录内缺可执行文件或缺运行时目录时，启用监控会按原口径拒绝，不会延迟到 `stay_open` 桥接启动期才失败。

UI 配置页保存时仍原样保留你填的目录（不改写为绝对路径），解析只在 Worker 加载时发生——发布包换目录解压后不会残留旧机器的绝对路径。

构建发版侧对应 `--exiftool-path-override <目录>`（`scripts/publish.sh`）与 `-ExifToolPathOverride <目录>`（`scripts/publish-app.ps1`）：直接使用该目录打包、跳过下载，并做 `exiftool -ver` 版本探针。两个来源参数互斥（同时给会显式报错）；与 `--exiftool-archive` / `-ExifToolZip` 也互斥。

**覆盖模式的信任边界**：该目录被视为完全可信——只校验「可执行文件 + 伴随目录（`exiftool_files/` 或 `lib/`）」的布局，不校验归档来源、签名或内容完整性；跳过 SHA256 钉扎是刻意的（用户自带版本未必是发布包钉死的那个）。`exiftool -ver` 探针失败或缺 `perl` 的构建主机只告警、不阻断打包。默认下载通道不受影响，仍强制按钉死 SHA256 校验。

默认下载通道的 SHA256 钉死值由 `scripts/verify-exiftool-download.sh` 与官方 `https://exiftool.org/checksums.txt` 对账（CI 在发布脚本变更时触发，另做周常巡检）；该脚本的硬判定是校验值一致，下载 URL 可达性只作软检查并如实标注。

## 校验分层

- 与监控无关的项（连接池大小、被禁的 `extra_exiftool_args`）在任何形态下都校验；
- 监控资源（`exiftool.path`、`exiftool_files`/`lib`、`quarantine.directory`、`audit.log_directory`、备份目录冲突）只在**启用监控**时校验——空监控目录的待机态不会因为它们缺失而拒绝启动（#100、#108）。
