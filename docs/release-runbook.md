# 发布 Runbook：手动打包链路（发布顺序 + 校验步骤）

> 本票载体：#26（迁移自治理清理前的 backlog 载体，原文件已删除，issue #26 是唯一存续记录）。
> 发版动作只在发版机上由人执行，agent 不主动触发。事实源是三个脚本本身：
> `scripts/release-readiness.ps1`（门禁）、`scripts/publish-app.ps1`（Windows）、`scripts/publish.sh`（Unix）。
> 脚本改了，本文同步改。

## 0. 前置条件

| 平台 | 工具链 | 说明 |
| --- | --- | --- |
| Windows（发 win-x64） | .NET 10 SDK、Rust（`rustup target add x86_64-pc-windows-gnu`）、MinGW `windres` | `windres` 须在 PATH 中，或位于脚本自动探测的 `C:\msys64\mingw64\bin` / `C:\ProgramData\mingw64\mingw64\bin`；两处都没有直接失败（windows-gnu 清单嵌入必需）。注意脚本预检不读 `WINDRES` 变量，只设它没用 |
| Linux / macOS | .NET 10 SDK、Rust（宿主默认 target）、perl | 构建主机没有 perl 只警告不失败，但目标机必须有 perl 才能跑内置 ExifTool |

硬约束（脚本内置，违反即报错）：

- Windows 只发 `win-x64`。gpui 不支持 win-x86 / win-arm64，也不支持交叉编译——Linux/macOS 必须在对应平台上跑 `publish.sh`，RID 由构建主机推导，`--runtime` 与主机不符直接退出。
- Framework 固定 `net10.0`。
- 发布属性只经脚本 `/p:` 传入，禁写回 csproj。

## 1. 发布顺序

### 第 1 步：确认基线干净

```powershell
git status   # 工作区干净，HEAD 即要发的提交
```

### 第 2 步：确认质量门

门禁脚本 `scripts/release-readiness.ps1`（内含 dotnet test → smoke dry-run → publish-app）**只覆盖 Windows**。发 Unix 包前没有现成门禁，先确认 CI 全绿（或本机补跑 `dotnet test PhotoPrivacy.sln`），再打包。

### 第 3 步：打包（平台二选一）

**Windows**（`scripts/publish-app.ps1`；发版机可直接用门禁一步到位，见下）：

```powershell
# 最简（把 1.2.3 换成要发的版本号）：
powershell -ExecutionPolicy Bypass -File scripts/publish-app.ps1 -Version 1.2.3 -Runtime win-x64
# 可选参数按需追加：-ExifToolVersion 13.61、-ExifToolZip <本地 zip 路径>、-ExifToolPathOverride <已安装 ExifTool 目录>
```

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `-Runtime` | `win-x64` | 唯一合法值 |
| `-SelfContained` / `-Zip` | `true` / `true` | 一般不动 |
| `-ExifToolVersion` | `13.61` | 随包内置的 ExifTool 版本，必须在钉扎表里（见 §2） |
| `-ExifToolZip` | 空 | 本地已下载的 zip（离线/内网），仍校验 SHA256 |
| `-ExifToolPathOverride` | 空 | 自带 ExifTool 目录直接打包，跳过下载与 SHA256 校验 |

`-ExifToolZip` 与 `-ExifToolPathOverride` 互斥，同时给会报错（防止一个被静默忽略）。

Windows 一键门禁（测试 + 冒烟 + 打包连跑，任何一步失败即停并打印输出尾部）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/release-readiness.ps1 -Version <版本>
```

**Linux / macOS**（`scripts/publish.sh`）：

```bash
# 最简（把 1.2.3 换成要发的版本号）：
scripts/publish.sh --version 1.2.3
# 可选参数按需追加：--exiftool-version 13.61、--exiftool-archive <本地 tar.gz>、--exiftool-path-override <目录>
# （--exiftool-archive 与 --exiftool-path-override 互斥）
```

RID 由构建主机自动推导（linux-x64 / linux-arm64 / osx-x64 / osx-arm64）。`--version` 写进包名和 Worker 程序集元数据（Rust UI 产物不带版本元数据）。`--exiftool-archive` 与 `--exiftool-path-override` 互斥；`--exiftool-archive` 要与匹配的 `--exiftool-version` 搭配（钉扎值按版本号查）。

两个脚本做的事相同（顺序）：

1. 清空 `release/<rid>/` 重建；
2. `cargo build --release --locked -p photoprivacy-ui`（Windows 额外指定 `--target x86_64-pc-windows-gnu`）；
3. `dotnet publish` Worker（单文件、self-contained）；
4. UI 产物复制为包根入口 `PhotoPrivacy(.exe)`，Worker 复制为包根 `PhotoPrivacyWorker(.exe)`；
5. 随包放安装脚本与配置（见 §3 布局）；
6. 内置 ExifTool（下载 → SHA256 校验 → 解包到 `<包根>/ExifTool/`，打包期就断言文件齐不齐）；
7. 删掉临时 worker 目录（注意：在 ExifTool 步骤之后才删，ExifTool 步骤失败时 `release/<rid>/worker/` 会残留，属正常）；
8. 压缩出 `release/PhotoPrivacy-<版本>-<rid>.zip`（Windows）或 `.tar.gz`（Unix）。

### 第 4 步：产物校验（逐项过）

1. **包存在且只此一份**：`release/PhotoPrivacy-<版本>-<rid>.zip` / `.tar.gz`。
2. **ExifTool 校验值对得上**，按来源看输出：
   - Windows 下载通道：`ExifTool archive verified on attempt N/N`；3 次取满仍不符会抛 `ExifTool archive download failed after 3 attempts (expected sha256 ...)`，这是下载通道被污染/拦截面（见 §2），不要放宽判定，重跑即可。
   - Windows 本地 `-ExifToolZip`：校验失败报 `ExifTool archive SHA256 mismatch`——查归档来源，不是网络问题。
   - Unix：成功打印 `Bundled ExifTool <版本>`；下载失败报 `ExifTool download failed`，校验失败报 `ExifTool archive SHA256 mismatch`（Unix 没有镜像轮换，先查网络和归档来源再重跑）。
   - 覆盖模式（两平台）：无校验值输出，只有 `-ver` 版本探针行或 WARNING。
3. **包内布局齐全**（对照 §3 清单，缺一项就是打包脚本断了，停下来查，不要手补）。
4. **入口能起**：包根直接运行 `PhotoPrivacy(.exe)`，确认新 UI 起得来、能连上 Worker（Windows 重点是应用清单生效——gpui 依赖 comctl32 v6，缺清单启动即失败）。
5. **视觉冒烟**（Windows 可选但建议）：按 `docs/agents/ui-smoke.md` 的口径过一遍。
6. **强杀门禁**（Windows）：⚠️ **当前不可用**——`verify-force-kill-cleanup.ps1` 取包路径是 `publish\`，而打包产物落在 `release\`，直接跑必报 `Package not found`（脚本历史遗留 bug，修复前跳过这步）。

### 第 5 步：安装（目标机）

随包 `scripts/` 里的安装脚本按平台选：

- Windows：`install-service.ps1`（必须与同目录 `service-worker-match.ps1` 一起在，卸载时的孤儿进程清理依赖它，包内已自动带上；按脚本位置自动推断包根）
- Linux：`install-systemd-service.sh`
- macOS：`install-launchd-service.sh`

Unix 的安装脚本默认找 `/opt/photoprivacy/PhotoPrivacyWorker`：解压到该安装目录，或按脚本说明传安装目录参数，否则报 `Missing executable`。注意 `--install-dir` 不会跟着改 config 路径（默认仍指 `/opt/photoprivacy/config/config.json`），自定义安装目录时要同时传 `--config <安装目录>/config/config.json`。

## 2. ExifTool 钉扎校验值（发版前必须核对）

两份钉扎表，**新增版本要同时登记两处**，校验值取自 <https://exiftool.org/checksums.txt>（Windows zip 包和 Unix tar.gz 是不同归档，校验值不同；注意 checksums.txt 只保留当前版本，历史版本可用 `scripts/verify-exiftool-download.sh` 连同两表一起实测核对）：

- `scripts/publish-app.ps1` 的 `$exifToolSha256` 哈希表（Windows zip）
- `scripts/publish.sh` 的 `exiftool_sha256()` case 函数（Unix tar.gz）

未登记的版本直接失败，这是防替换/防半截归档的闸，不要绕。覆盖模式（`-ExifToolPathOverride` / `--exiftool-path-override`）是唯一例外：用户自带任意版本，跳过 SHA256，仅校验目录布局并做 `-ver` 版本探针（探针失败只警告）。

Windows 下载通道不稳（#164）：SourceForge 可能返回跳转/拦截页，脚本按「直链镜像（`?viasf=1`）→ 常规地址」轮换重试 3 次，取满次数 SHA256 仍不符才失败。镜像返回任何别的内容都会被拒掉。Unix 下载通道只有单一直链 + `curl --retry 3`。

## 3. 包内布局清单

**Windows（zip 根）**：

```
PhotoPrivacy.exe              ← UI 入口（cargo windows-gnu 产物改名）
PhotoPrivacyWorker.exe        ← Worker 单文件
README.md
config/config.json            ← config.sample.json 的副本（热目录为空，启动即待机，用前需配置）
config/config.sample.json
config/README.md
ExifTool/exiftool.exe         ← 官方包内 exiftool(-k).exe 改名
ExifTool/exiftool_files/      ← 私有 Perl 运行时（内含 LICENSE）
ExifTool/README.txt           ← 官方归档自带时才有，非固定项
scripts/install-service.ps1
scripts/service-worker-match.ps1
```

**Unix（tar.gz 根）**：

```
PhotoPrivacy                  ← UI 入口
PhotoPrivacyWorker            ← Worker 单文件
README.md
config/{config.json, config.sample.json, README.md}
ExifTool/exiftool             ← Perl 脚本（目标机需 perl）
ExifTool/lib/
ExifTool/README
scripts/install-systemd-service.sh   （osx-* 则为 install-launchd-service.sh）
```

配置模板的 `exiftool.path` 留空时，运行时探测优先命中 `<包根>/ExifTool/`，裸机不用预装 ExifTool。

## 4. 常见坑速查

- `windres not found` → 把 windres 所在目录加进 PATH，或装到自动探测目录（§0）；只设 `WINDRES` 变量没用，脚本预检不读它。
- `Invalid Runtime 'win-x86'` → gpui 不支持，换 x64 机器发。
- Unix 上 `Runtime linux-x64 requires a Linux x86_64 build host` → 不支持交叉编译，到对应平台发。
- `No pinned SHA256 for ExifTool x.y` → 先登记两处钉扎表（§2）再发版。
- `ExifTool archive download failed after N attempts`（Windows）→ 下载通道被污染/拦截面，重跑脚本即可（自动换镜像重试），不要手工放宽校验。
- `ExifTool archive SHA256 mismatch` → Windows 上出自 `-ExifToolZip` 本地归档，Unix 上出自下载或 `--exiftool-archive`；查归档来源，不是重试能解决的。
- 覆盖模式报「互斥」 → `-ExifToolZip` 与 `-ExifToolPathOverride`（Unix 对应 `--exiftool-archive` 与 `--exiftool-path-override`）只能传一个。
- `Bundled ExifTool incomplete`（仅 Windows）→ 包内 ExifTool 缺 `exiftool.exe` / `exiftool_files\exiftool.pl` / `LICENSE` 之一，查归档来源。
- 脚本被 PowerShell 执行策略拒绝 → 命令带 `-ExecutionPolicy Bypass`（§1 第 3 步的写法）。
- `release/`、`logs/`、`bin/`、`obj/` 是本地产物，不提交。
