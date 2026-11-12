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

### 3. release-readiness.ps1 全链：未测（本机无法跑）；CI 等价证据已留，是否算数待裁决

按票面验收，本项要本机跑 `scripts/release-readiness.ps1` 并打出
`Release readiness passed.`。这台机器没装 dotnet 和 cargo，脚本第 1 步
`dotnet test` 就起不来，故本机未跑、此项不记通过。等价证据（同一 main
`bdda51c`，同一 Release 配置，run 43169200573）：

- `[1/3] dotnet test PhotoPrivacy.sln --configuration Release`（排除
  Smoke/ExifTool 类别，与脚本一致）——`test` job 通过。
- `[2/3] smoke test (dry-run)`——即上面第 1 项，通过。
- `[3/3] publish app + worker`——`build (win-x64)` job 跑的就是
  `scripts/publish-app.ps1`（`-Zip true`），Publish 与 Verify structure 两步均过，
  产物结构含 `PhotoPrivacy.exe`、`PhotoPrivacyWorker.exe`、
  `config/config.sample.json`、`ExifTool/exiftool.exe`。
- `[4/4] force-kill cleanup gate`：`release-readiness.ps1` 默认不跑
  （`-IncludeForceKillCheck false`）；单独跑也跑不了——runbook 第 6 条登记了
  脚本取包路径（`publish\`）与产物目录（`release\`）不一致的历史问题，修复前跳过。

要补本机原样一轮，需发版机按 `docs/release-runbook.md` 由人执行
`scripts/release-readiness.ps1`；补跑前本项维持「未测」。

## 脚本改动说明（前一轮引入，现役行为）

- `scripts/smoke.ps1` 增加可选 `-WorkerPath`：显式给出时直接启动该可执行文件
  （发布链单文件 exe 或 DLL），`-LiteralPath` 按字面处理路径；不给该参数时仍走
  原 DLL-first 探测，`release-readiness.ps1` 等既有调用点不用改。
- 同一脚本把 `--dry-run` 布尔值统一小写输出（Worker `ParseBool` 两种写法解析等价），
  并把 DLL/exe 两个启动分支合并为一次 `Start-Process`。
