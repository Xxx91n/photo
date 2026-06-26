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
│   ├── PhotoPrivacy.Ipc/      # 进程间通信（命名管道）
│   ├── PhotoPrivacy.Worker/   # Windows 服务 / 后台 Worker
│   └── PhotoPrivacy.Ui/       # Avalonia UI 桌面应用
├── tests/
│   ├── PhotoPrivacy.Core.Tests/        # 单元测试
│   └── PhotoPrivacy.IntegrationTests/  # 集成测试
├── config/                    # 配置文件（config.json 不提交）
├── scripts/                   # PowerShell 部署/发布脚本
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

```powershell
# 构建
dotnet build PhotoPrivacy.sln

# 运行所有测试
dotnet test PhotoPrivacy.sln

# 运行单元测试
dotnet test tests/PhotoPrivacy.Core.Tests/

# 运行集成测试
dotnet test tests/PhotoPrivacy.IntegrationTests/

# 发布
.\scripts\publish-app.ps1
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

---

## 7. Git 规范

- **分支**: `feature/xxx`, `fix/xxx`, `refactor/xxx`
- **提交信息**: 中文或英文均可，简洁描述变更内容
- **不要自动提交** — 除非用户明确要求
- **提交前检查**: 运行安全扫描，确保无新增 SCS 警告

---

## 8. 文件系统约定

| 路径 | 用途 | 是否提交 |
|------|------|----------|
| `config/config.json` | 本地配置（含 ExifTool 路径） | ❌ |
| `config/config.sample.json` | 配置模板 | ✅ |
| `opencode.json` | OpenCode agent 配置 | ❌ (.gitignore) |
| `AGENTS.md` | Agent 项目规范 | ❌ (.gitignore) |
| `docs/superpowers/` | 设计文档和计划 | ✅ |
| `publish/` | 发布输出 | ❌ |
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
| ExifTool | `D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe` |
| .NET SDK | 10.0.201 |
| Semgrep | pip install（Python 3.11+） |
| Python | `D:\DevTools\Python\runtimes\pythoncore-3.11-64\` |

---

## 11. 参考资料

- [Semgrep OSS 文档](https://semgrep.dev/docs/getting-started/cli-oss)
- [SecurityCodeScan 文档](https://security-code-scan.github.io)
- [SecurityCodeScan NuGet](https://www.nuget.org/packages/SecurityCodeScan.VS2019)
- [Semgrep GitHub](https://github.com/semgrep/semgrep)
- [Datadog SAIST（AI-native SAST）](https://github.com/DataDog/datadog-saist) — 等 C# 支持后可集成
