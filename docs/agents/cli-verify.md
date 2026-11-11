# CLI 模式实机验证（#6）

> 记录纪律沿用 `docs/agents/ui-smoke.md`「怎么记结果」：每项记 通过 / 失败 / 未测 /
> 不迁移（裁决），没有证据不填通过；证据写「日期 + 通道 + 看到的具体现象」。
> 本文件只装 #6 这一轮；后续轮次从 git 历史取旧记录。

## 轮次信息

- 日期：2026-11-12（本机时区 +08:00；审计内 UTC 时间戳为 2026-11-11T21:15Z）
- 机器：Windows 实机（win32 10.0.20348 x64）
- 通道：**CI 发布产物 + smoke.ps1**。产物取自 Release workflow
  （`create_release=false`，run
  [43093454490](https://github.com/Xxx91n/photo/actions/runs/43093454490)，
  `release-win-x64` artifact，head = main `a6796cb`，与本票基线一致）。
  本机按授权不做编译（dotnet build/test/publish 都不跑），Worker 用产物里的
  self-contained 单文件 `PhotoPrivacyWorker.exe` 直接启动——smoke.ps1 本轮为此新增
  `-WorkerPath` 参数（DLL-first 行为不变，不给该参数时走原探测）。
- Worker 启动方式：`scripts/smoke.ps1 -WorkerPath <产物>\PhotoPrivacyWorker.exe`，
  CLI 参数 `--mode cli --hot-folder ... --audit-folder ... --once true --dry-run <true|false>`
  （脚本原样拼出并打印，见各项输出）。
- ExifTool：未传任何路径参数。产物内置 `<包根>/ExifTool/exiftool.exe`（13.61），
  运行时探测第一位即命中（审计事件 `exiftool_started` 里的 `exe_path` 为证）。

## 逐项结果

### 1. smoke.ps1 dry-run：通过

2026-11-12，本机 + CI 产物。`scripts/smoke.ps1 -DryRun true -WorkerPath <产物exe>` 跑完：

```
Smoke test start
Smoke worker args: --mode cli --hot-folder "...\smoke1\hot" --audit-folder "...\smoke1\audit" --once true --dry-run true
----- worker stdout (tail) -----
[INF] Application started. Press Ctrl+C to shut down.
[INF] Hosting environment: Production
[INF] Content root path: C:\photo-t6
[INF] Application is shutting down...
Smoke framework: net10.0
Smoke test done
```

全程没有 `Worker CLI failed (exit code ...)`；`Smoke test done` 只在退出码 0 后打印
（脚本判定逻辑），Worker 退出码 0。

### 2. 真处理一轮（去 dry-run）：通过

2026-11-12，本机 + CI 产物。造图：System.Drawing 生成 320x240 JPEG，用产物内置
ExifTool 13.61 写入 `EXIF:Make/Model/DateTimeOriginal/Artist` 与 `XMP:Creator`
（处理前快照确认全部在位，文件 5.7 kB）。`scripts/smoke.ps1 -DryRun false` 跑完
退出码 0，当轮控制台输出原文：

```
Smoke test start
Smoke worker args: --mode cli --hot-folder "...\smoke2\hot" --audit-folder "...\smoke2\audit" --once true --dry-run false
----- worker stdout (tail) -----
[INF] Application started. Press Ctrl+C to shut down.
[INF] Hosting environment: Production
[INF] Content root path: C:\photo-t6
[INF] Application is shutting down...
Smoke framework: net10.0
Smoke test done
```

日志与审计对得上：Worker 日志（`logs/worker-20261112.log`，本地产物不提交）当轮
`05:15:17.070 +08:00 Application started` → `05:15:21.829 +08:00 Application is
shutting down`，与审计 jsonl 的 UTC 时刻逐秒吻合（`service_started`
21:15:17.189Z、`file_processing_succeeded` 21:15:19.710Z、`service_stopped`
21:15:21.909Z，均 +8h 即本地 05:15:17 / 05:15:19 / 05:15:21）。当轮核对：

- 审计 `audit-2026-11-12.jsonl` 出现成功事件（原文）：
  ```json
  {"event_type":"file_processing_succeeded","level":"INFO","timestamp_utc":"2026-11-11T21:15:19.7107796+00:00","task_id":"f90a232998964b35ac4e7dc925553e23","source_path_masked":"C:\\photo-t6-verify\\runs\\smoke2\\hot\\photo.jpg","source_path_hash":"65905FBB081152A4","message":"attempt=1;wipe_result=Cleaned_Modified","data":null,"diagnostic_mode":false}
  ```
- `processed.ndjson` 增长一行，原文：
  ```json
  {"key":"C:\\photo-t6-verify\\runs\\smoke2\\hot\\photo.jpg","ts":"2026-11-11T21:15:19.7113821\u002B00:00","v":2}
  ```
- 原件进备份镜像：`hot\bak\photo.jpg.bak`（5.7 kB），处理后快照确认 Make、
  Camera Model Name、Artist、Date/Time Original、XMP Creator 全部还在。
- 原地文件被真清洗：`hot\photo.jpg`（2.6 kB），上述五个字段全部消失
  （exiftool 处理后快照，仅剩结构类标签）。

### 3. release-readiness.ps1 全链：未测（本机）；CI 等价证据已留，是否算数待裁决

按票面验收，本项要本机跑 `scripts/release-readiness.ps1` 并打出
`Release readiness passed.`。该脚本 `[1/3]` 是 `dotnet test`、`[3/3]` 是
`publish-app.ps1`，都必然本机编译；本轮按授权「不编译、用 CI 产物」执行，
**本机没有跑过这个脚本，此项不记通过**。等价证据（同一 main `a6796cb`，同一
Release 配置）：

- `[1/3] dotnet test PhotoPrivacy.sln --configuration Release`（排除
  Smoke/ExifTool 类别，与脚本一致）——run 43093454490 `test` job 通过
  （2026-11-11T20:09Z–20:12Z）。
- `[2/3] smoke test (dry-run)`——即上面第 1 项，通过。
- `[3/3] publish app + worker`——同 run `build (win-x64)` job 跑的就是
  `scripts/publish-app.ps1`（`-Zip true`），Publish 与 Verify structure 两步均过
  （产物结构含 `PhotoPrivacy.exe`、`PhotoPrivacyWorker.exe`、
  `config/config.sample.json`）；首跑因 GitHub artifact 服务端故障（上传返回
  HTML 错误页）失败在 Upload artifact 步，`--failed` 重跑后全部通过。
- `[4/4] force-kill cleanup gate`：`release-readiness.ps1` 默认不跑
  （`-IncludeForceKillCheck false`），本轮同样未跑，与本项无关。

要补本机原样一轮，需发版机按 `docs/release-runbook.md` 由人执行
`scripts/release-readiness.ps1`；补跑前本项维持「未测」。

## 评审后修订（同日）

- smoke.ps1 本轮顺带把 `--dry-run` 的布尔值统一为小写输出（原先 `ToString()` 出
  `True`）。两种写法解析等价：Worker 的 `ParseBool` 先走 `bool.TryParse`
  （.NET 语义本身不分大小写），1/yes/on 再按 `OrdinalIgnoreCase` 比对
  （`src/PhotoPrivacy.Core/Worker/MetadataCleanerWorker.cs`），改动只为对齐票面
  样例 `--dry-run true` 的小写形态。
- 启动调用合并：DLL/exe 两分支合并为一次 `Start-Process`（只差 FilePath 与参数
  前缀，重定向等参数不再重复）。改完后用同参数复跑一轮 dry-run 确认行为不变：
  `Smoke test start` → `Smoke worker args: … --dry-run true` → `Smoke test done`，
  无 `Worker CLI failed`，输出存仓外 `runs/smoke1/recheck-output.txt`。
- PR 审查返修（CodeRabbit）：`-WorkerPath` 的存在性检查与解析改用
  `-LiteralPath`，避免产物路径里的 `[ ]` 被当通配符；脚本补 UTF-8 BOM（含中文
  注释后 PSScriptAnalyzer 要求，与本仓其他 PS 脚本一致）。复验：用
  `runs\recheck[1]\PhotoPrivacyWorker.exe` 这种带方括号的路径实跑一轮 dry-run
  通过，输出存仓外 `runs/smoke1/literalpath-recheck.txt`。
