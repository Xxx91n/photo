# CLI 模式实机验证（#6）

> 记录纪律沿用 `docs/agents/ui-smoke.md`「怎么记结果」：每项记 通过 / 失败 / 未测 /
> 不迁移（裁决），没有证据不填通过；证据写「日期 + 通道 + 看到的具体现象」。
> 本文件只装 #6 当前一轮；上一轮（2026-11-12 凌晨，main `a6796cb` 产物）从 git 历史
> 取：`git show bdda51c:docs/agents/cli-verify.md`。

## 轮次信息

- 日期：2026-11-12 下午（本机时区 +08:00；审计内 UTC 时间戳为 2026-11-12T06:53Z）
- 机器：Windows 实机（win32 10.0.20348 x64）
- 通道：**CI 发布产物 + smoke.ps1**。产物取自 Release workflow
  （`create_release=false`，run
  [43169200573](https://github.com/Xxx91n/photo/actions/runs/43169200573)，
  `release-win-x64` artifact，head = main `bdda51c`，即当前 main）。
  本机按授权不做编译（本机也没装 dotnet/cargo），Worker 用产物里的
  self-contained 单文件 `PhotoPrivacyWorker.exe` 直接启动
  （`scripts/smoke.ps1 -WorkerPath <产物>\PhotoPrivacyWorker.exe`）。
- ExifTool：未传任何路径参数。产物内置 `<包根>/ExifTool/exiftool.exe`（13.61），
  运行时探测第一位即命中（审计事件 `exiftool_started` 里的 `exe_path` 为证）。

## 逐项结果

### 1. smoke.ps1 dry-run：通过

2026-11-12 14:53，本机 + CI 产物。`scripts/smoke.ps1 -DryRun true -WorkerPath <产物exe>`
跑完，当轮控制台输出原文：

```
Smoke test start
Smoke worker args: --mode cli --hot-folder "C:\photo-t6-verify\runs2\smoke1\hot" --audit-folder "C:\photo-t6-verify\runs2\smoke1\audit" --once true --dry-run true
----- worker stdout (tail) -----
[INF] Application started. Press Ctrl+C to shut down.
[INF] Hosting environment: Production
[INF] Content root path: C:\photo-t6b
[INF] Application is shutting down...
Smoke framework: net10.0
Smoke test done
```

全程没有 `Worker CLI failed (exit code ...)`；`Smoke test done` 只在退出码 0 后打印
（脚本判定逻辑），Worker 退出码 0。

### 2. 真处理一轮（去 dry-run）：通过

2026-11-12 14:53，本机 + CI 产物。造图：System.Drawing 生成 320x240 JPEG，用产物内置
ExifTool 13.61 写入 `EXIF:Make/Model/DateTimeOriginal/Artist` 与 `XMP:Creator`
（处理前快照确认全部在位，文件 5.7 kB）。`scripts/smoke.ps1 -DryRun false` 跑完，
当轮控制台输出原文：

```
Smoke test start
Smoke worker args: --mode cli --hot-folder "C:\photo-t6-verify\runs2\smoke2\hot" --audit-folder "C:\photo-t6-verify\runs2\smoke2\audit" --once true --dry-run false
----- worker stdout (tail) -----
[INF] Application started. Press Ctrl+C to shut down.
[INF] Hosting environment: Production
[INF] Content root path: C:\photo-t6b
[INF] Application is shutting down...
Smoke framework: net10.0
Smoke test done
```

当轮核对：

- 审计 `audit-2026-11-12.jsonl` 成功事件原文：
  ```json
  {"event_type":"file_processing_succeeded","level":"INFO","timestamp_utc":"2026-11-12T06:53:48.7693240+00:00","task_id":"17da688f73434e7db99897133e7f75fd","source_path_masked":"C:\\photo-t6-verify\\runs2\\smoke2\\hot\\photo.jpg","source_path_hash":"BB0C6798D509907B","message":"attempt=1;wipe_result=Cleaned_Modified","data":null,"diagnostic_mode":false}
  ```
- `processed.ndjson` 增长一行，原文：
  ```json
  {"key":"C:\\photo-t6-verify\\runs2\\smoke2\\hot\\photo.jpg","ts":"2026-11-12T06:53:48.7706536\u002B00:00","v":2}
  ```
- 日志与审计对得上：Worker 日志（`logs/worker-20261112.log`，本地产物不提交）当轮
  `14:53:45.173 +08:00 Application started` → `14:53:51.097 +08:00 Application is
  shutting down`，与审计 jsonl 的 UTC 时刻逐秒吻合（`service_started`
  06:53:45.369Z、`file_processing_succeeded` 06:53:48.769Z、`service_stopped`
  06:53:51.182Z，均 +8h 即本地 14:53:45 / 14:53:48 / 14:53:51）。
- 原件进备份镜像：`hot\bak\photo.jpg.bak`（5.7 kB），处理后快照确认 Make、
  Camera Model Name、Artist、Date/Time Original、XMP Creator 全部还在。
- 原地文件被真清洗：`hot\photo.jpg`（2.6 kB），上述五个字段全部消失
  （exiftool 处理后快照，仅剩结构类标签）。

### 3. release-readiness.ps1 全链：通过

2026-11-12 傍晚，本机 + 本地工具链（.NET SDK 10.0.201、Rust `x86_64-pc-windows-gnu`、
MinGW、Windows SDK 的 fxc.exe；本轮补装，见文末）。命令与当轮输出（按最终代码重跑的
那轮，两轮均通过、退出码 0）：

```
[1/3] dotnet test PhotoPrivacy.sln
  已通过! - 失败: 0，通过: 322，已跳过: 0，总计: 322，持续时间: 42 s - PhotoPrivacy.Core.Tests.dll (net10.0)
  已通过! - 失败: 0，通过: 197，已跳过: 0，总计: 197，持续时间: 4 m 54 s - PhotoPrivacy.IntegrationTests.dll (net10.0)
[2/3] smoke test (dry-run)
  Smoke test start
  Smoke worker args: --mode cli --hot-folder "...\photo-release-smoke-...\hot" --audit-folder "...\audit" --once true --dry-run true
  Smoke test done
[3/3] publish app + worker
  Downloading ExifTool 13.61 ...
  ExifTool archive verified on attempt 1/3 (11205455 bytes)
  Bundled ExifTool 13.61 -> C:\photo-t6b\release\win-x64\ExifTool
  Package created: C:\photo-t6b\release\PhotoPrivacy-0.1.0-verify6c-win-x64.zip
  Publish done: C:\photo-t6b\release\win-x64
Release readiness passed.
```

脚本退出码 0。`[4/4] force-kill cleanup gate` 未跑：`release-readiness.ps1` 默认
`-IncludeForceKillCheck false`，且 runbook 第 6 条登记了脚本取包路径（`publish\`）
与产物目录（`release\`）不一致的历史问题，修复前跳过。

首次跑通前修掉的四处（都在本 PR 里，原因见「脚本与测试改动说明」）：
`release-readiness.ps1` 的 Windows 测试过滤、`publish-app.ps1` 的 5.1 stderr 兼容、
`BackupMirrorLayoutTests` 的宿主前提判断、Smoke 发布测试的管道挂死与退役断言。

## 脚本与测试改动说明

- `scripts/smoke.ps1` 增加可选 `-WorkerPath`：显式给出时直接启动该可执行文件
  （发布链单文件 exe 或 DLL），`-LiteralPath` 按字面处理路径；不给该参数时仍走
  原 DLL-first 探测，`release-readiness.ps1` 等既有调用点不用改。
- 同一脚本把 `--dry-run` 布尔值统一小写输出（Worker `ParseBool` 两种写法解析等价），
  并把 DLL/exe 两个启动分支合并为一次 `Start-Process`。
- `scripts/release-readiness.ps1`：Windows 上 `dotnet test` 带与 ci.yml 相同的排过滤
  （`WindowsServicePipeAclTests`、`ServicePipeNameEntropyTests` 在 Windows 上会挂死
  测试宿主）；Linux/macOS 仍全量跑。
- `scripts/publish-app.ps1`：`rustup`/`cargo`/`dotnet publish` 三处原生命令调用期间
  临时放宽 `$ErrorActionPreference`（5.1 会把它们的 stderr 进度当终止错误）。
- `tests/PhotoPrivacy.Core.Tests/Pipeline/BackupMirrorLayoutTests.cs`：保留名用例先判
  宿主前提（部分 .NET/OS 会把 `CON.jpg` 归一成 `\\.\CON`），前提不成立时改钉安全退路
  `_unsorted`，不再假装通过。
- `tests/PhotoPrivacy.IntegrationTests/Smoke/ReleaseReadinessScriptValidationTests.cs`：
  旧 Avalonia 托盘资产断言改成核验现役发布入口（与 release.yml 的 Verify structure
  同一组文件）；子进程输出改经临时 .cmd 落文件再读，不再等管道 EOF（子进程再拉起的
  常驻服务器会继承管道句柄，导致 ReadToEndAsync/WaitForExitAsync 永久挂起）。
