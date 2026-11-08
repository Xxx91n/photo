# 发布 Runbook：手动打包链路（发布顺序 + 校验步骤）

> 本票载体：#26（原 `docs/backlog/open-items-2026-10-14.md` 第 27 项）。
> 发版动作只在发版机上由人执行，agent 不主动触发。事实源是三个脚本本身：
> `scripts/release-readiness.ps1`（门禁）、`scripts/publish-app.ps1`（Windows）、`scripts/publish.sh`（Unix）。
> 脚本改了，本文同步改。

## 0. 前置条件

| 平台 | 工具链 | 说明 |
| --- | --- | --- |
| Windows（发 win-x64） | .NET 10 SDK、Rust（`rustup target add x86_64-pc-windows-gnu`）、MinGW `windres` | `windres` 须在 PATH 或 `WINDRES` 指定；脚本会自动探测 `C:\msys64\mingw64\bin` 与 `C:\ProgramData\mingw64\mingw64\bin`，都找不到直接失败（windows-gnu 清单嵌入必需） |
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

### 第 2 步：跑发版门禁（Windows 发版机）

```powershell
powershell -File scripts/release-readiness.ps1 -Version <版本> [-IncludeForceKillCheck true]
```

门禁内部顺序（任何一步失败即停，stdout/stderr 尾部会打印出来）：

1. `dotnet test PhotoPrivacy.sln`（全量测试）
2. `smoke.ps1`（临时目录 + dry-run，跑完自动清理）
3. `publish-app.ps1`（真实打包，产物落 `release/win-x64/`）
4. 可选 `-IncludeForceKillCheck true`：强杀清理门禁 `verify-force-kill-cleanup.ps1`（仅 win-*）

门禁过了，产物其实已经打包完成；下面第 3 步是绕过门禁单独打包的用法（如需自定义 ExifTool 参数）。

### 第 3 步：打包（平台二选一）

**Windows**（`scripts/publish-app.ps1`）：

```powershell
powershell -File scripts/publish-app.ps1 -Version <版本> -Runtime win-x64 `
  [-ExifToolVersion 13.61] [-ExifToolZip <本地 zip 路径>] [-ExifToolPathOverride <已安装 ExifTool 目录>]
```

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `-Runtime` | `win-x64` | 唯一合法值 |
| `-SelfContained` / `-Zip` | `true` / `true` | 一般不动 |
| `-ExifToolVersion` | `13.61` | 随包内置的 ExifTool 版本，必须在钉扎表里（见 §2） |
| `-ExifToolZip` | 空 | 本地已下载的 zip（离线/内网），仍校验 SHA256 |
| `-ExifToolPathOverride` | 空 | 自带 ExifTool 目录直接打包，跳过下载与 SHA256 校验 |

`-ExifToolZip` 与 `-ExifToolPathOverride` 互斥，同时给会报错（防止一个被静默忽略）。

**Linux / macOS**（`scripts/publish.sh`）：

```bash
scripts/publish.sh [--version <版本>] \
  [--exiftool-version 13.61 | --exiftool-archive <本地 tar.gz> | --exiftool-path-override <目录>]
```

RID 由构建主机自动推导（linux-x64 / linux-arm64 / osx-x64 / osx-arm64），`--version` 只写进包名。

两个脚本做的事相同（顺序）：

1. 清空 `release/<rid>/` 重建；
2. `cargo build --release --locked -p photoprivacy-ui`（Windows 额外指定 `--target x86_64-pc-windows-gnu`）；
3. `dotnet publish` Worker（单文件、self-contained）；
4. UI 产物复制为包根入口 `PhotoPrivacy(.exe)`，Worker 复制为包根 `PhotoPrivacyWorker(.exe)`，临时 worker 目录删掉；
5. 随包放安装脚本与配置（见 §3 布局）;
6. 内置 ExifTool（下载 → SHA256 校验 → 解包到 `<包根>/ExifTool/`，打包期就断言文件齐不齐）；
7. 压缩出 `release/PhotoPrivacy-<版本>-<rid>.zip`（Windows）或 `.tar.gz`（Unix）。

### 第 4 步：产物校验（逐项过）

1. **包存在且只此一份**：`release/PhotoPrivacy-<版本>-<rid>.zip` / `.tar.gz`。
2. **ExifTool 校验值对得上**：脚本输出 `ExifTool archive verified on attempt N/N`（Windows）或 `Bundled ExifTool <版本>`（Unix）；出现 `SHA256 mismatch` 说明下载通道取到了别的东西（见 §2），不要放宽判定，重跑。
3. **包内布局齐全**（对照 §3 清单，缺一项就是打包脚本断了，停下来查，不要手补）。
4. **入口能起**：包根直接运行 `PhotoPrivacy(.exe)`，确认新 UI 起得来、能连上 Worker（Windows 重点是应用清单生效——gpui 依赖 comctl32 v6，缺清单启动即失败）。
5. **视觉冒烟**（Windows 可选但建议）：按 `docs/agents/ui-smoke.md` 的口径过一遍。
6. **强杀门禁**（Windows、发服务版建议）：`release-readiness.ps1 -IncludeForceKillCheck true`。

### 第 5 步：安装（目标机）

随包 `scripts/` 里的安装脚本按平台选：

- Windows：`install-service.ps1`（必须与同目录 `service-worker-match.ps1` 一起在，卸载时的孤儿进程清理依赖它，包内已自动带上）
- Linux：`install-systemd-service.sh`
- macOS：`install-launchd-service.sh`

## 2. ExifTool 钉扎校验值（发版前必须核对）

两份钉扎表，**新增版本要同时登记两处**，校验值取自 <https://exiftool.org/checksums.txt>（Windows zip 包和 Unix tar.gz 是不同归档，校验值不同）：

- `scripts/publish-app.ps1` 的 `$exifToolSha256` 哈希表（Windows zip）
- `scripts/publish.sh` 的 `exiftool_sha256()` case 函数（Unix tar.gz）

未登记的版本直接失败，这是防替换/防半截归档的闸，不要绕。覆盖模式（`-ExifToolPathOverride` / `--exiftool-path-override`）是唯一例外：用户自带任意版本，跳过 SHA256，仅校验目录布局并做 `-ver` 版本探针（探针失败只警告）。

Windows 下载通道不稳（#164）：SourceForge 可能返回跳转/拦截页，脚本按「直链镜像（`?viasf=1`）→ 常规地址」轮换重试 3 次，取满次数 SHA256 仍不符才失败。镜像返回任何别的内容都会被拒掉。

## 3. 包内布局清单

**Windows（zip 根）**：

```
PhotoPrivacy.exe              ← UI 入口（cargo windows-gnu 产物改名）
PhotoPrivacyWorker.exe        ← Worker 单文件
README.md
config/config.json            ← config.sample.json 的副本，开箱即用
config/config.sample.json
config/README.md
ExifTool/exiftool.exe         ← 官方包内 exiftool(-k).exe 改名
ExifTool/exiftool_files/      ← 私有 Perl 运行时（内含 LICENSE）
ExifTool/README.txt
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

- `windres not found` → 装 MinGW 或设 `WINDRES`（§0）。
- `Invalid Runtime 'win-x86'` → gpui 不支持，换 x64 机器发。
- Unix 上 `Runtime linux-x64 requires a Linux x86_64 build host` → 不支持交叉编译，到对应平台发。
- `No pinned SHA256 for ExifTool x.y` → 先登记两处钉扎表（§2）再发版。
- `SHA256 mismatch` → 下载通道被污染/拦截面，重跑脚本即可（自动换镜像重试），不要手工放宽校验。
- 覆盖模式报「互斥」 → `-ExifToolZip` 与 `-ExifToolPathOverride` 只能传一个。
- `Bundled ExifTool incomplete` → 包内 ExifTool 缺 `exiftool.exe` / `exiftool_files\exiftool.pl` / `LICENSE` 之一，查归档来源。
- `release/`、`logs/`、`bin/`、`obj/` 是本地产物，不提交。
