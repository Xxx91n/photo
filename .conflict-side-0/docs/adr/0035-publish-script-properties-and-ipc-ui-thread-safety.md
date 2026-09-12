# 发布脚本恢复 PublishSingleFile + IPC/UI 线程安全规则

ADR 0012 决策将 PublishSingleFile/SelfContained 等发布属性写入 csproj PropertyGroup，但 SelfContained=true 在无 RID 的 dotnet build 下破坏输出路径。实际实现回退到发布脚本 /p: 传递，但发布脚本未保留这些标志，导致 Worker 打包为 159KB 裸 apphost、依赖丢失、无法启动。本 ADR 固化最终实现方案。

## Considered Options

- **方案 A（采纳）**: 发布属性留在脚本 /p: 传递（Worker 每次 publish 显式 /p:PublishSingleFile=true + /p:IncludeNativeLibrariesForSelfExtract=true + /p:PublishTrimmed=false）。保持 csproj 不下沉 SelfContained，避免 `dotnet build` 输出路径被 RID 目录污染。
- **方案 B（否决）**: 属性入 csproj（ADR 0012 原方案）——SelfContained=true 在无 RID 的 build 下输出到 bin/Debug/net10.0/<rid>/ 破坏测试和 IDE 增量构建

## Consequences

- 修改 ADR 0012：实际实现改为方案 A，csproj 不持有 SelfContained/PublishSingleFile
- 发布脚本（publish-app.ps1 + publish.sh）是 single source of truth，必须保留三个 /p: 标志
- 回归根因：git a60b666 移除 csproj 属性后未恢复到脚本，本次提交 38142f9 修复

## companion: NamedPipeIpcTransport 移除 ACL

- 移除 NamedPipeServerStreamAcl.Create 路径，改用普通 NamedPipeServerStream
- ACL WorldSid ReadWrite 仅 service 模式跨用户（service as LocalSystem, UI as user）需要；background 模式同用户不需要
- ACL 在单文件解压上下文抛 UnauthorizedAccessException 且泄漏管道实例（catch fallback 也失败），直到 254 实例上限，worker 永久 stuck
- service 模式需要跨用户时再单独引入可配置 ACL（未来 ADR）

## companion: UI 线程 IPC 调用禁止 sync-over-async

- 禁止在 UI 线程调用 Worker IPC 的方法使用 `.GetAwaiter().GetResult()` 或 `.Result`
- WorkerIpcClient.SendAsync 加 3s 读超时（LinkedTokenSource.CancelAfter），防止 worker 接受连接但不回响应时无限阻塞 UI 线程
- TrayHost.UpdateMenu/TogglePauseResume 改为 async + Dispatcher.UIThread 更新 NativeMenuItem.Header
- TrayHost.IsVisible=true 延迟到 Dispatcher.UIThread.Post(Background)，避免 Avalonia 11.1.3 TrayIcon.IsVisible setter 在 InitializeRuntime 中死锁 UI 线程
- 审计日志 tail 的 getLogLevel lambda 用 Dispatcher.UIThread 包裹跨线程 DataContext 访问