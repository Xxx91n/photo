# 配置说明

发布模板会有意将 `watch.hot_folder` 留空。Worker 会保持 IPC 连接并进入待机状态，UI 会提示你前往配置页选择监控目录。选择有效目录并保存配置后，Worker 会通过热重载启动监控，无需重启。

## `exiftool.path` 留空的含义

模板里 `exiftool.path` 为空串，表示**按运行时探测**：

1. 发布包内置的 `<包根>/ExifTool/`（Windows 为 `exiftool.exe` + `exiftool_files/`；Linux/macOS 为 `exiftool` + `lib/`，需系统有 `perl`）；
2. 系统安装位置（Windows 的 `C:\Program Files\ExifTool\` 等；Linux/macOS 的 `/usr/bin`、`/usr/local/bin`、`/opt/homebrew/bin` 等 PATH 可见位置）。

手填的绝对路径始终优先，不会被探测覆盖。内置的 ExifTool 被删或被安全软件隔离时，保存监控目录会以可读原因失败（提示指向重新解压发布包或安装 ExifTool），该提示常驻显示，不会几秒后消失。

## 校验分层

- 与监控无关的项（连接池大小、被禁的 `extra_exiftool_args`）在任何形态下都校验；
- 监控资源（`exiftool.path`、`exiftool_files`/`lib`、`quarantine.directory`、`audit.log_directory`、备份目录冲突）只在**启用监控**时校验——空监控目录的待机态不会因为它们缺失而拒绝启动（#100、#108）。
