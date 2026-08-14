# ADR 0047: 性能优化——GC调优 + CompiledBindings + R2R + Lazy ExifTool + Dispose加固 + 缓存容量

**状态**: 计划中（grill 已确认待实施）
**日期**: 2026-08-14

## 背景

开发者感知性能问题：启动速度慢、内存占用 ~80MB、进程不及时释放、缺缓存机制。
kimi_k26 调研行业 .NET 10 Avalonia tray app 性能心智模型（2 次 research query），
审计代码现状（csproj / runtimeconfig / Dispatcher / ExifTool / Cache）。

## 调研来源

- kimi_k26 pro research × 2：.NET 10 Avalonia GC 调优、CompiledBindings、R2R、
  stay_open pool lazy init、UI Dispatcher 批量合并、缓存 bounded cache 模式
- 引用：Microsoft Workstation/Server GC 文档、Avalonia CompiledBindings 文档、
  Avalonia 性能 tips blog、ExifTool stay_open forum

## 决策（6 项 grill 方案 A/A/A/A/A/A）

### 1. GC 调优：runtimeconfig.template.json

新增 src/PhotoPrivacy.Ui/runtimeconfig.template.json 和
src/PhotoPrivacy.Worker/runtimeconfig.template.json：

```json
{
  "runtimeOptions": {
    "gcServer": false,
    "gcConcurrent": true
  }
}
```

Workstation GC 对桌面 tray app 是行业标准，减少 Server GC 多堆固定开销。
预期内存降低 10-20MB。

### 2. CompiledBindings + ReadyToRun

- Directory.Build.props 加 <PublishReadyToRun>true</PublishReadyToRun>（全项目 R2R 预编译 IL）
- UI csproj 加 <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
- R2R 增加 ~10-20% 体积但减少 JIT 启动开销；CompiledBindings 零体积零代码改动
- 预期冷启动降低 20-40%

### 3. UI Dispatcher 批量合并

审计 MainWindow.axaml.cs 的 20+ Dispatcher.UIThread.Post/InvokeAsync 调用点，
对高频路径（日志/审计尾随/服务状态）统一用批量合并（coalescing render scheduler）。
已有 commit a635ac0 的日志批量优化，扩展到其他逐条 Post 路径。
预期减少帧率抖动和 CPU 峰值。

### 4. ExifTool stay_open pool lazy 初始化

把 _bridge.StartAsync(stoppingToken) 从 ExecuteAsync 移除，
改为在 EnqueueIfNeeded 首次被调用时才 lazy spawn。
SemaphoreSlim + volatile bool _bridgeStarted 保证只启动一次。
Worker 启动到首个文件到达期间省下 ExifTool 进程内存（~20-30MB）+ spawn 延迟（~500ms-2s）。

### 5. 进程释放/Dispose 加固

- ExifToolBridge 实现 IDisposable（dispose _startLock SemaphoreSlim）
- PooledExifToolBridge.Dispose 遍历 _bridges 调用每个 bridge.Dispose
- Worker reload 路径：旧 bridge 先 StopAsync 再 Dispose 再赋新值
- 修复 CA2213 SemaphoreSlim 泄漏（正常 StopAsync 路径已完善，仅异常/reload 路径遗漏）

### 6. 缓存容量上限

RecentFingerprintCache 加 max capacity（默认 10000 条）+ 接入已有 _cleanupTimer。
超容量时按最旧优先 LRU 驱逐。Dictionary + lock 不引入新依赖（ponytail 原则）。
验收：热文件夹 10000+ 文件时内存稳定不增长。单元测试覆盖容量驱逐。

## 实施路线图

| 序号 | 改动 | 文件 | 验收标准 |
|------|------|------|----------|
| M1 | runtimeconfig.template.json GC调优 | Ui + Worker 新文件 | dotnet build + publish 验证 GC 设置生效 |
| M2 | CompiledBindings + R2R | Directory.Build.props + Ui.csproj | dotnet build 无新增 warning + 启动加速 |
| M3 | UI Dispatcher 批量合并 | MainWindow.axaml.cs + AuditTailService 等 | 帧率稳定、无逐条 Post 残留高频路径 |
| M4 | ExifTool lazy spawn | MetadataCleanerWorker.cs | 首文件到达前无 ExifTool 进程 |
| M5 | Dispose 加固 | ExifToolBridge + PooledExifToolBridge + Worker reload | CA2213 消除 + 测试全过 |
| M6 | 缓存容量上限 | RecentFingerprintCache.cs + cleanup 集成 | 单元测试覆盖 eviction |

## test 闭环

每个 M 完成后：
1. dotnet build 0 error
2. dotnet test PhotoPrivacy.sln 全过（单元 + 集成）
3. 新增/扩展单元测试覆盖改动点（如 lazy spawn、cache eviction、dispose）
4. 每个里程 commit，最终 push

## 后果

- UI 内存从 ~80MB 降低到 ~50MB 以下（GC + lazy ExifTool + 缓存容量）
- 冷启动加速 20-40%（CompiledBindings + R2R + lazy ExifTool）
- 帧率稳定（Dispatcher 批量合并）
- 无进程/资源泄漏（Dispose 加固）
- 缓存有上限不会无限增长（max capacity + LRU）
