# AGENTS.md — PhotoPrivacy Project Specification

> 本文件是 AI agent 在此项目中工作的核心规范。每次会话开始时必须阅读。

---

## 1. 项目概述

**PhotoPrivacy** 是一个 C#/.NET 10 桌面工具，用于批量清理照片/视频的 EXIF 元数据和隐私信息。

- **技术栈**: C# 13 / .NET 10 / Avalonia UI 11 / ExifTool / Named Pipes IPC
- **仓库**: https://github.com/Xxx91n/photo
- **语言**: 所有与用户的交互必须使用中文

---

## 2. 架构

```
PhotoPrivacy.sln
├── src/
│   ├── PhotoPrivacy.Core/     # 核心逻辑：ExifTool桥接、文件监控、元数据处理
│   ├── PhotoPrivacy.Ipc/      # 进程间通信（命名管道 + Unix 域套接字）
│   ├── PhotoPrivacy.Worker/   # 跨平台 Worker 服务（Windows/Linux/macOS）
│   └── PhotoPrivacy.Ui/       # Avalonia UI 桌面应用
├── tests/
│   ├── PhotoPrivacy.Core.Tests/        # 单元测试
│   └── PhotoPrivacy.IntegrationTests/  # 集成测试
├── config/                    # 配置文件（config.json 不提交）
├── scripts/                   # 部署/发布脚本（PowerShell + sh，跨平台）
└── docs/                      # 文档和设计规范
```

### 关键模块风险点

| 模块 | 风险 | 安全规则 |
|------|------|----------|
| ExifTool 桥接 | 命令注入 (`Process.Start`) | SCS0001, Semgrep `csharp.lang.security.injection.command` |
| 文件监听 | 路径穿越 | SCS0018, Semgrep `csharp.lang.security.path-traversal` |
| IPC 命名管道 | 认证缺失、不安全反序列化 | SCS0028 |
| config.json | 密钥泄漏、路径篡改 | gitleaks, Semgrep hardcoded-secrets |
| Worker 多线程 | 竞争条件、资源泄漏 | Roslyn CA 系列 |

---

## 3. 安全扫描技能（MANDATORY）

本项目集成了两套安全分析工具。**在修改涉及安全敏感模块的代码后，agent 必须主动运行安全扫描。**

### 3.1 Semgrep OSS — 模式匹配 SAST

```powershell
# 完整扫描（C# 安全规则 + 通用安全审计）
semgrep scan --config p/csharp --config p/security-audit --json ./src

# 快速扫描单文件
semgrep scan --config p/csharp --json ./src/PhotoPrivacy.Core/ExifToolBridge.cs

# 仅高危
semgrep scan --config p/csharp --json --severity ERROR ./src
```

- **输出格式**: JSON，关键字段 `check_id`, `path`, `start.line`, `message`, `severity`
- **适用场景**: 代码审查、PR 前检查、安全审计
- **安装**: `pip install semgrep`（Python 3.10+）

### 3.2 SecurityCodeScan — Roslyn 安全分析器

SecurityCodeScan 已通过 `Directory.Build.props` 嵌入所有项目，`dotnet build` 时自动运行。

```powershell
# 构建并捕获安全警告
dotnet build PhotoPrivacy.sln --no-incremental 2>&1 | Select-String 'SCS'

# 输出到文件
dotnet build PhotoPrivacy.sln --no-incremental 2>&1 | Out-File build-security.txt
```

- **关键规则**:
  - `SCS0001` — 命令注入（ExifTool `Process.Start`）
  - `SCS0006` — 弱随机数
  - `SCS0018` — 路径穿越
  - `SCS0028` — 不安全反序列化
- **安装**: 已通过 NuGet `SecurityCodeScan.VS2019` 集成

### 3.3 组合安全扫描流程

```powershell
# Step 1: Roslyn 分析（编译期，零额外成本）
dotnet build PhotoPrivacy.sln --no-incremental 2>&1 | Select-String 'SCS'

# Step 2: Semgrep 深度扫描（补充跨文件模式检测）
semgrep scan --config p/csharp --config p/security-audit --json ./src

# Step 3: 分析两个输出，解释每个发现，提供修复
```

---

## 4. 构建与测试

> **重要**：发布前必须先运行测试或 `release-readiness.ps1`（含 test+smoke+publish 完整 gate）。
> `publish-app.ps1` 内部调用 `dotnet publish`（含编译），但**跳过完整 test 验证**。
> 仅运行 publish 而不先 test，会导致 bug 已修但发布的二进制仍是旧版。
> 正确流程：**构建 → 测试 → 发布**（或直接用 `release-readiness.ps1` 一键 gate）。

```powershell
# 构建
dotnet build PhotoPrivacy.sln

# 运行所有测试
dotnet vstest tests\PhotoPrivacy.Core.Tests\bin\Debug\net10.0\PhotoPrivacy.Core.Tests.dll tests\PhotoPrivacy.IntegrationTests\bin\Debug\net10.0\PhotoPrivacy.IntegrationTests.dll --settings:test.runsettings /Platform:x64

# 运行单元测试
dotnet vstest tests\PhotoPrivacy.Core.Tests\bin\Debug\net10.0\PhotoPrivacy.Core.Tests.dll --settings:test.runsettings

# 运行集成测试
dotnet vstest tests\PhotoPrivacy.IntegrationTests\bin\Debug\net10.0\PhotoPrivacy.IntegrationTests.dll --settings:test.runsettings

# 发布（仅打包，不含测试验证）
.\scripts\publish-app.ps1   # 输出到 release/<rid>/（Windows）
./scripts/publish.sh        # 输出到 release/<rid>/（Linux/macOS）

# 发布前一键 gate（test + smoke + publish，推荐）
.\scripts\release-readiness.ps1 -Version 0.1.0-preview -Runtime win-x64
```

---

## 5. 编码规范

### 必须遵守

1. **语言**: C# 13，启用 `Nullable` 和 `ImplicitUsings`
2. **命名**:
   - PascalCase: 类、方法、属性、公共成员
   - camelCase: 局部变量、参数
   - _camelCase: 私有字段（带下划线前缀）
3. **异步**: 优先使用 `async/await`，避免 `.Result` 和 `.Wait()`
4. **日志**: 使用 `Microsoft.Extensions.Logging`，结构化日志
5. **配置**: 通过 `IConfiguration` / `IOptions<T>` 注入，禁止硬编码路径

### ExifTool 桥接规则

1. **必须使用 `stay_open` 模式** — 禁止每文件 spawn 进程
2. **必须验证 ExifTool 路径** — 来自 config.json，需检查存在性和合法性
3. **禁止 shell 执行** — `Process.StartInfo.UseShellExecute = false`
4. **参数转义** — 所有文件路径必须正确转义

### 文件系统规则

1. **文件监听必须使用 FSW 事件驱动** — 禁止轮询
2. **路径验证** — 防止路径穿越（`..`、符号链接）
3. **长路径处理** — Windows 长路径前缀 `\\?\`
4. **资源释放** — `FileStream` 等必须 `using` 或 `await using`

### IPC 规则

1. **命名管道需认证** — 验证连接方身份
2. **消息验证** — 反序列化前校验格式和大小
3. **超时处理** — 避免无限等待
4. **禁止 sync-over-async on UI 线程** — 禁止在 UI 线程调用 Worker IPC 使用 .GetAwaiter().GetResult() 或 .Result（见 ADR 0035）。WorkerIpcClient.SendAsync 已加 3s 读超时兜底
5. **NamedPipe 不使用 ACL** — Background 模式同用户不需要 WorldSid ACL；NamedPipeServerStreamAcl.Create 在单文件解压上下文抛 UnauthorizedAccessException 并泄漏管道实例（见 ADR 0035）
6. **TrayIcon.IsVisible 延迟设置** — 必须用 Dispatcher.UIThread.Post(Background) 延迟，避免 Avalonia 11.1.3 在 InitializeRuntime 中死锁 UI 线程
7. **Service 模式必须配置文件日志** — 禁止用 `if (mode != RuntimeMode.Service)` 跳过 Serilog File sink；服务崩溃时 stdout 不可见会导致完全无诊断信息（见 ADR 0036）
8. **Background 和 Service 必须使用独立 Mutex** — 禁止共享同一个 `Global\PhotoPrivacyWorker_Instance`；它们有独立的 IPC 端点，共存时不能互相阻塞（见 ADR 0036）

9. **禁止"应用配置"手动按钮** — 配置变更必须防抖即时写盘（500ms），不依赖用户手动点击（见 ADR 0037）
10. **清空日志必须重置 AuditTailService 读取偏移** — 仅清内存 ObservableCollection 不够，restart 后旧日志会重新填充（见 ADR 0037）
11. **GetRecentLogs Backfill 由 AuditTailService 统一收口** — 历史补位逻辑（backfillFetcher fetch → MergeBackfillLines 去重排序 → 锁内原子认领水位）驻留在 `AuditTailService`，fire-and-forget 不阻塞 UI 线程；Worker 不可达按 null 契约静默重试，失败不杀 UI（见 ADR 0037/0046/0056）
---

## 6. 禁止事项

| 禁止 | 原因 |
|------|------|
| 提交 secrets / .env / credentials | 安全风险 |
| 每文件 spawn ExifTool 进程 | 性能灾难 |
| 文件监听使用轮询 | 性能问题 |
| 触碰 ExifToolGUI 目录 | 外部工具，不属于本项目 |
| 使用 `Process.Start` 且 `UseShellExecute = true` | 命令注入风险 |
| 硬编码文件路径 | 可移植性 |
| `Thread.Sleep` 在异步上下文 | 阻塞线程池 |
| UI 线程 sync-over-async（`.GetAwaiter().GetResult()` / `.Result`）调用 Worker IPC | 死锁 UI 线程（见 ADR 0035） |
| Service 模式跳过 Serilog File sink | 服务崩溃无文件日志，无法诊断（见 ADR 0036） |
| Background 和 Service 共享同一 Mutex | Background 持有 Mutex 时 Service 无法启动（见 ADR 0036） |
| 发布脚本只复制 config.sample.json 不复制 config.json | 服务 binPath 引用 config.json 不存在，Worker 启动失败 |
| install-service.ps1 使用 `"""` 三重引号拼接 binPath | PowerShell 5.1 ParserError，脚本无法执行 |
| GitButler 历史改写/丢弃操作（move/undo/resolve cancel/squash/discard/uncommit/branch delete/pull）前不先快照 .scratch 流程产物 | .scratch 不受版本控制，栈手术曾整树蒸发（ADR 0058 事故 1）；双轨防护见 .scratch/architecture-recovery/WORKFLOW.md §4.4 |

| 依赖手动"应用配置"按钮做配置持久化 | 用户忘记点击 → 重启后配置丢失（见 ADR 0037） |
| ClearLogs 只清内存不重置 _lastPosition | FSW 下次轮询重新填充旧日志→隐私泄露（见 ADR 0037） |
| AuditTailService 初始化从头读取历史日志 | 重启后敏感历史重现（见 ADR 0037） |
---

## 7. Git 规范

- **分支**: `feature/xxx`, `fix/xxx`, `refactor/xxx`
- **提交信息**: 中文或英文均可，简洁描述变更内容
- **不要自动提交** — 除非用户明确要求
- **提交前检查**: 运行安全扫描，确保无新增 SCS 警告
- **流程产物持久化**: .scratch/ 不受版本控制；流程文件定稿即沉淀 docs/process/（副本随票提交），执行 GitButler 历史改写/丢弃类操作前必须先做仓库外快照（触发时机与动作见 .scratch/architecture-recovery/WORKFLOW.md §4.4）

---

## 8. 文件系统约定

| 路径 | 用途 | 是否提交 |
|------|------|----------|
| `config/config.json` | 本地配置（含 ExifTool 路径） | ❌ |
| `config/config.sample.json` | 配置模板 | ✅ |
| `opencode.json` | OpenCode agent 配置 | ❌ (.gitignore) |
| `AGENTS.md` | Agent 项目规范 | ✅ |
| `docs/adr/` | 架构决策记录（ADR） | ✅ |
| `release/` | 发布输出 | ❌ |
| `logs/` | 运行日志 | ❌ |

---

## 9. 安全审查检查清单

在修改安全敏感代码后，agent 应执行：

- [ ] `dotnet build` 无新增 SCS 警告
- [ ] `semgrep scan` 无新增 HIGH/ERROR 发现
- [ ] ExifTool 调用使用 `stay_open` + 参数转义
- [ ] 文件路径经过验证（无穿越、无注入）
- [ ] IPC 消息经过验证和大小限制
- [ ] 无硬编码密钥或路径
- [ ] 资源正确释放（using/Dispose）
- [ ] 异常处理不吞掉安全相关异常

---

## 10. 外部工具路径

| 工具 | 路径 |
|------|------|
| ExifTool | `config.json` 配置（默认搜索 `C:\Program Files\ExifTool\exiftool.exe` 等标准位置） |
| .NET SDK | 10.0.201 |
| Semgrep | pip install（Python 3.11+） |
| Python | 3.11+（pip install semgrep，PATH 可用） |
| 流程快照（WORKFLOW §4.4 轨 2） | `node scripts/workflow-snapshot.js [源目录] [输出根]`（动栈前强制；整树复制 + manifest 逐文件字节数/SHA256，快照落仓库外 photo-snapshots/） |
| 快照校验（WORKFLOW §4.4 轨 2） | `node scripts/workflow-verify.js <快照目录> [源目录]`（missing/changed/added + ZERO-LOSS 判定，非 0 退出码 = 丢失） |

---

## 11. 参考资料

- [Semgrep OSS 文档](https://semgrep.dev/docs/getting-started/cli-oss)
- [SecurityCodeScan 文档](https://security-code-scan.github.io)
- [SecurityCodeScan NuGet](https://www.nuget.org/packages/SecurityCodeScan.VS2019)
- [Semgrep GitHub](https://github.com/semgrep/semgrep)
- [Datadog SAIST（AI-native SAST）](https://github.com/DataDog/datadog-saist) — 等 C# 支持后可集成
