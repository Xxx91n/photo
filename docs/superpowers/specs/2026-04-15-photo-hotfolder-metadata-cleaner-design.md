# PC热文件夹自动元数据清理工具（MVP-1）设计规格

## 1. 文档信息
- 日期：2026-04-15
- 版本：v1
- 范围：MVP-1（Windows）
- 目标交付：单引擎 + 双宿主（CLI + Windows Service）

## 2. 目标与非目标

### 2.1 目标
- 监听指定热文件夹中的文件变更，自动清除元数据以保护隐私。
- 基于 ExifTool `-stay_open` 维持单一持久进程，避免每文件启动新进程。
- 使用事件驱动（FileSystemWatcher），禁止轮询。
- 支持失败重试后隔离，保持主目录整洁。
- 支持输出目录可配置；默认输出到源目录并避免重复处理。
- 支持可选备份：开启后生成 `.bak` 备份文件。
- 同时提供 CLI 与 Windows Service 两种运行形态，复用同一核心逻辑。

### 2.2 非目标
- 不在 MVP-1 提供托盘 UI。
- 不在 MVP-1 实现 Linux/macOS 宿主。
- 不在 MVP-1 实现数据库，仅使用文件日志与内存状态。

## 3. 硬性约束
- 默认 ExifTool 路径：`D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe`。
- 启动前必须校验 ExifTool 可执行文件与同级 `exiftool_files` 目录存在。
- 禁止触碰 `ExifToolGUI` 目录的任何文件（只读/执行）。
- 禁止每文件 spawn ExifTool；必须使用单一 `-stay_open true -@ -`。
- 文件监听必须是 FSW 事件驱动；禁止轮询作为常规机制。
- 所有规则与扩展参数必须配置驱动，不硬编码在业务分支中。

## 4. 方案总览（单引擎 + 双宿主）

### 4.1 项目结构
```text
src/
  PhotoPrivacy.Core/      # 核心业务库
  PhotoPrivacy.Cli/       # 前台常驻 CLI 宿主
  PhotoPrivacy.Service/   # Windows Service 宿主
```

### 4.2 模块职责
- `Watcher`：封装 FileSystemWatcher，投递事件，处理 Error 恢复。
- `DebounceQueue`：路径级去抖，保证文件静默后再处理。
- `RuleEngine`：白名单、排除规则、输出策略、备份策略、重试策略决策。
- `Pipeline`：任务状态机编排（重试、隔离、审计串接）。
- `ExifToolBridge`：持久 ExifTool 进程管理与任务协议执行。
- `AuditLogger`：JSONL 审计日志（UTC + 脱敏）。
- `Configuration`：加载、校验、迁移与有效配置输出。

## 5. 数据流与状态机

### 5.1 主流程
```text
FSW Event
  -> DebounceQueue
  -> RuleEngine(eligible?)
  -> Pipeline
  -> ExifToolBridge
  -> Success/Retry/Quarantine
  -> AuditLogger
```

### 5.2 文件状态机
```text
Detected -> Debounced -> Eligible -> Processing -> Succeeded
                                      |
                                      v
                                  FailedOnce -> Retrying -> (Succeeded | Quarantined)
```

### 5.3 失败处置（已确认）
- 首次失败：记录失败审计，按退避策略重试。
- 重试超限仍失败：移动到隔离目录，记录二次失败/隔离审计。
- 目标：避免临时占用误判，同时保持主目录最终干净。

### 5.4 输出策略（已确认）
- `same_as_source`（默认）：输出到源目录。
- `fixed_directory`：输出到指定目录（保留相对路径结构）。

### 5.5 同目录防重复处理机制
- `inflight set`：正在处理的路径不重复入队。
- `recent fingerprint cache`：缓存 `path + size + lastWriteUtc`（TTL 可配）。
- `self-write suppression window`：清理完成后的短窗口抑制二次事件。
- 三层同时启用，避免“自触发循环”。

## 6. ExifToolBridge 设计

### 6.1 启动参数基线
```text
"D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe"
  -stay_open true -@ -
  -q -q
  -API WindowsLongPath=1
  -API LargeFileSupport=1
```

### 6.2 任务协议
- 每任务分配唯一 `taskId`。
- 任务命令块尾部追加 `-echo1 TASK_DONE_<taskId>` 与 `-execute`。
- Bridge 异步逐行读取 stdout；命中 `TASK_DONE_<taskId>` 视为完成。
- 不依赖 `{ready}`，规避 Windows stdout 缓冲导致的死等待。

### 6.3 版本探测与兼容
- Bridge 启动后执行一次 `-ver` 并记录版本。
- 若版本处于已知风险窗口（如 13.05 附近的 WindowsLongPath 兼容问题），记录警告。
- 允许通过配置关闭 `WindowsLongPath` API 作为兼容降级手段。

### 6.4 并发与恢复
- ExifTool 单进程串行执行；上层可并发入队。
- 单任务超时触发失败回传，由 Pipeline 执行重试/隔离。
- 进程异常退出时自动重建 Bridge，并对在途任务安全失败回传。

## 7. FSW 监听与溢出恢复

### 7.1 参数建议
- 监听事件：`Created`、`Changed`、`Renamed`、`Error`。
- `InternalBufferSize = 65536`（64KB）。
- `NotifyFilter`：`FileName | LastWrite | Size | DirectoryName`。

### 7.2 Error 恢复闭环（硬要求）
1. 记录 `fsw_error` 审计。
2. `Dispose()` 旧 watcher。
3. 重建 watcher 并重新订阅。
4. 执行一次全量补偿扫描并入 DebounceQueue。
5. 记录 `fsw_recovered` 审计。

说明：全量扫描仅用于恢复补偿，不得演变为周期轮询。

## 8. 配置模型

### 8.1 顶层结构（config.json）
```json
{
  "schema_version": 1,
  "exiftool": {
    "path": "D:\\tools\\A_system\\ExifToolGUI\\ExifTool\\ExifTool.exe",
    "enable_windows_long_path": true,
    "enable_large_file_support": true,
    "extra_exiftool_args": []
  },
  "watch": {
    "hot_folder": "D:\\hot",
    "include_subdirectories": true,
    "debounce_ms": 800,
    "internal_buffer_size": 65536
  },
  "rules": {
    "allowed_extensions": [".jpg", ".jpeg", ".png", ".heic", ".mp4", ".pdf", ".docx"],
    "excluded_patterns": ["~$*", "*.tmp"],
    "output_mode": "same_as_source",
    "output_directory": ""
  },
  "retry": {
    "max_attempts": 3,
    "backoff_seconds": [1, 3, 10]
  },
  "backup": {
    "enabled": false,
    "suffix": ".bak",
    "retention": "keep"
  },
  "quarantine": {
    "enabled": true,
    "directory": "D:\\hot\\_quarantine"
  },
  "audit": {
    "log_directory": "D:\\hot\\_audit",
    "retain_days": 30,
    "diagnostic_mode": false
  }
}
```

### 8.2 配置校验
- `exiftool.path` 必须绝对路径且可访问。
- 禁止 `extra_exiftool_args` 覆盖关键参数：`-stay_open`、`-@ -`、`-execute`、`-echo1`。
- 当 `output_mode = same_as_source` 时，强制开启防重复处理机制。
- `backup.suffix` 默认 `.bak`，仅允许白名单后缀。

### 8.3 配置迁移
- 通过 `schema_version` 执行向前迁移，缺省字段自动补默认值。
- 提供有效配置输出（例如 CLI `--print-effective-config`）便于排障。

## 9. 备份与输出语义

### 9.1 backup = false
- 直接在目标文件上执行清理（默认 `-overwrite_original` 路线）。

### 9.2 backup = true
- 在处理前创建 `.bak` 备份（文件命名为 `originalName.ext.bak`）。
- 清理成功后根据 `backup.retention` 决定保留或删除 `.bak`。
- 清理失败并隔离时，`.bak` 强制保留，支持人工恢复。

备注：具体“重命名再输出”或“复制后输出”由实现阶段按同卷原子性和恢复复杂度选定，但外部行为必须保持上述契约。

## 10. 审计日志与可观测性

### 10.1 日志格式
- JSON Lines，UTC 时间戳。
- 按天切分，按保留天数清理。

### 10.2 核心事件
- 文件链路：`file_detected`、`file_processing_started`、`file_processing_succeeded`、`file_processing_failed`、`file_retry_scheduled`、`file_quarantined`。
- 系统链路：`fsw_error`、`fsw_recovered`、`exiftool_started`、`exiftool_restarted`、`exiftool_version_warning`、`service_started`、`service_stopped`。

### 10.3 脱敏策略
- 路径字段默认掩码与稳定哈希，不记录明文完整路径。
- 诊断模式可增加细节，但仍必须执行路径脱敏。

## 11. 测试与验收标准（MVP-1）

### 11.1 功能验收
- 白名单命中可处理，非白名单可跳过并记录。
- 失败后重试，重试失败后隔离并二次审计。
- 输出到源目录时不发生重复处理循环。
- 开启备份时 `.bak` 语义符合配置。

### 11.2 可靠性验收
- ExifTool 崩溃后自动重启，后续任务持续可处理。
- FSW 缓冲溢出后完成“重建 + 补偿扫描 + recovered 审计”闭环。
- 宿主重启后可以继续接收并处理新文件。

### 11.3 性能验收
- 小文件：1000 x 100KB，记录总耗时与 P50/P95。
- 大文件：100 x 50MB，确认只在写入完成后处理一次。
- 混合负载：图片/视频/文档混投，验证规则正确率与隔离准确率。

### 11.4 双宿主一致性验收
- CLI 与 Service 在同配置下产出一致结果与一致审计语义。

## 12. 里程碑
- M1（本阶段）：Core + CLI + Service + 配置 + 审计 + 基础验收。
- M2：托盘 UI（配置编辑、状态查看、告警提示）。
- M3：Linux 服务化（后续阶段）。

## 13. 主要风险与缓解
- 风险：FSW 高峰溢出导致漏事件。
  - 缓解：64KB 缓冲 + Error 重建 + 全量补偿扫描。
- 风险：ExifTool 特定版本与 `WindowsLongPath` 兼容问题。
  - 缓解：版本探测告警 + 配置化降级开关。
- 风险：同目录输出导致重复触发循环。
  - 缓解：inflight + 指纹缓存 + 自写抑制窗口三层机制。

## 14. 开放决策（实现阶段收敛）
- 备份实现采用“重命名再输出”还是“复制后输出”，由实现计划阶段根据原子性与吞吐测试结果最终落定。
- 隔离移动失败（跨卷/权限）时采用复制+删除补偿，或本地重试队列，需在实现计划中明确。
