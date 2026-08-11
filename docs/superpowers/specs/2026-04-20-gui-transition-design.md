# PhotoPrivacy GUI 过渡收口设计（成熟交互优先）

## 1. 文档信息
- 日期：2026-04-20
- 版本：v1
- 阶段：过渡版（Transition），非最终全新 GUI 版
- 目标：在保持三进程架构的前提下，恢复到“成熟可交付 GUI”状态，优先复用已有代码，去除开发环境痕迹

## 2. 背景与问题

当前项目已经完成关键架构演进：

- `PhotoPrivacy.exe`：纯 GUI 入口
- `PhotoPrivacyWorker.exe --mode background`：后台 Worker
- `PhotoPrivacyWorker.exe --mode service`：服务 Worker

架构方向正确，但 GUI 仍存在“迁移中状态”的痕迹：

1. 交互层面混入过渡性控件/文案，和历史成熟版不完全一致；
2. 局部实现仍偏“开发态表达”（内部术语暴露、调试习惯残留）；
3. 用户可见路径中存在“可用但不够产品化”的细节。

本设计目标是：**不做全新视觉重构，仅做过渡收口**，以最小改动恢复成熟体验并保证可落地交付。

## 3. 目标

### 3.1 核心目标

1. **核心交互 1:1 还原优先**（对齐历史成熟 GUI 行为）
2. **尽量复用已有代码**（控制改动规模与 token 消耗）
3. **三进程边界不回退**（GUI/Background Worker/Service Worker 继续解耦）
4. **用户交付标准优先**（减少开发痕迹，默认路径与文档面向最终用户）

### 3.2 质量目标

- 运行稳定：模式切换、托盘生命周期、服务管理动作可预测
- 回归稳定：完整测试与脚本门禁通过
- 可维护：保留后续“全新 GUI 重做”空间，不在本阶段引入大规模 UI 架构重写

## 4. 非目标

1. 本阶段不做全新视觉语言或全量信息架构重构；
2. 本阶段不做跨平台 UI 体验统一重设计；
3. 本阶段不引入复杂新功能（例如多页面向导、深度主题系统）。

## 5. 硬性约束（必须满足）

来自 `docs/opencode.md` 与 `docs/Fllow-Rsearch.md` 的约束继续生效：

1. 中文输出；
2. ExifTool 默认路径约束：`D:\tools\A_system\ExifToolGUI\ExifTool\ExifTool.exe`；
3. 必须使用 ExifTool `stay_open` 单进程桥接，禁止每文件 spawn；
4. 文件监听必须事件驱动（FSW/inotify/FSEvents），禁止轮询；
5. 不触碰 ExifToolGUI 目录文件（仅读取/执行）；
6. 配置驱动行为，保留可扩展性与兼容性。

## 6. 方案选择与决策

采用“折中方案（核心交互 1:1，还原优先）”：

- **主策略**：以历史成熟 GUI 代码为基线恢复交互，尽量复用；
- **适配策略**：只在边界层接入新三进程 IPC 与 Worker 路径约束；
- **收口策略**：清理用户可见开发痕迹，不做重设计。

该策略满足“当前只过渡、后续再做全新 GUI”的阶段目标。

## 7. 目标架构（过渡版）

### 7.1 进程边界（保持不变）

```text
PhotoPrivacy.exe (GUI only)
  -> IPC -> PhotoPrivacyWorker.exe --mode background
  -> ServiceManager -> PhotoPrivacyWorker.exe --mode service
```

### 7.2 GUI 层职责（收口后）

- `UiProgram`：单实例 + Worker 连接/拉起 + 运行时选项组装
- `MainWindow`：成熟交互壳（日志、配置、服务管理、托盘入口）
- `TrayHost`：暂停/恢复/打开主窗口/退出
- `ServiceManager`：仅围绕 `PhotoPrivacyWorker(.exe)` 做服务安装与控制
- `WorkerIpcClient + WorkerProcessManager`：Worker 状态与控制通道

### 7.3 架构优化原则（本阶段）

1. 优化边界，不重写主体：
   - UI 逻辑优先复用；
   - IPC 与服务路径校验放在边界层；
2. 清理“开发态暴露”：
   - 用户文案不泄露内部术语；
   - 发布配置不默认展示调试取向；
3. 兼容优先：
   - 配置字段保持向后兼容；
   - 旧脚本入口可保留薄兼容壳（如已存在转发脚本）。

## 8. 详细设计

### 8.1 GUI 交互恢复（1:1 优先）

1. 以历史成熟版 `MainWindow` 布局为主，恢复稳定控件组织：
   - 顶部状态条（模式、运行状态、ExifTool 版本）；
   - 工具条（暂停/恢复、清空日志、打开配置目录、刷新服务状态）；
   - Tab（配置、日志、服务管理器）；
2. 新增但非必要控件默认收敛（避免过渡噪音）；
3. 服务管理器保留成熟动作语义（Install/Uninstall/Start/Stop），文案后续统一中文化可另开任务。

### 8.2 三进程适配（不回退）

1. 主窗口交互调用保留成熟行为，但底层统一走 Worker IPC；
2. 服务安装与启动目标强制指向 `PhotoPrivacyWorker.exe`；
3. UI 模式标签与状态文本对用户友好，不暴露内部实现细节。

### 8.3 产品化去痕

1. `UiProgram` 日志策略：发布态不默认输出开发追踪；
2. README 主路径改为用户使用手册视角（安装/启动/服务管理/验证）；
3. 保持脚本命名迁移后的一致性：`publish-app.ps1` 为主入口，旧脚本仅兼容转发。

### 8.4 配置与兼容

1. 继续保留 `ui.hide_main_window_on_startup` 与 `ui.hide_tray_icon`；
2. GUI 过渡版优先稳定交互，不引入更多新配置项；
3. 样例配置继续保留 ExifTool 固定默认路径。

## 9. 变更范围（文件级）

### 9.1 必改

- `src/PhotoPrivacy.Ui/Views/MainWindow.axaml`
- `src/PhotoPrivacy.Ui/Views/MainWindow.axaml.cs`
- `src/PhotoPrivacy.Ui/Program.cs`
- `src/PhotoPrivacy.Ui/TrayHost.cs`
- `src/PhotoPrivacy.Ui/ServiceManager.cs`
- `README.md`

### 9.2 视情况微调

- `src/PhotoPrivacy.Ui/ViewModels/MainWindowViewModel.cs`
- `src/PhotoPrivacy.Ui/BackgroundUiOptions.cs`
- `scripts/publish-cli-exe.ps1`（保留兼容壳，不做主流程）

## 10. 验收标准

### 10.1 用户体验验收

1. GUI 启动后交互与成熟版一致（核心路径）：
   - 暂停/恢复；
   - 日志查看与清空；
   - 配置保存；
   - 服务管理动作可执行；
2. 托盘行为稳定：
   - 最小化/关闭窗口不误退出；
   - 托盘菜单动作可用；
3. 不出现明显开发态文案或调试痕迹。

### 10.2 架构与稳定性验收

1. 仍满足三进程分离；
2. 服务安装与启动路径始终为 Worker 可执行文件；
3. 模式切换不残留旧进程。

### 10.3 自动化验收

```bash
dotnet test PhotoPrivacy.sln
powershell -ExecutionPolicy Bypass -File scripts/release-readiness.ps1 -Version 0.1.0-preview -Runtime win-x64
powershell -ExecutionPolicy Bypass -File scripts/verify-force-kill-cleanup.ps1 -Version 0.1.0-preview
```

## 11. 风险与缓解

1. 风险：过度回退导致新架构接口不兼容
   - 缓解：仅回退 UI 壳层，不回退 IPC/Worker 边界；
2. 风险：为省改动保留过多临时代码
   - 缓解：清理用户可见痕迹，技术债登记到后续“全新 GUI”里程碑；
3. 风险：脚本与文档出现双入口混淆
   - 缓解：统一主入口为 `publish-app.ps1`，兼容脚本仅提示转发。

## 12. 后续阶段（不在本次实现）

1. 全新 GUI 视觉与交互重构；
2. UI 层进一步模块化（如 `MainWindow` 控制器拆分）；
3. 跨平台 UI 一致性优化；
4. 更精细的可观测性与诊断面板设计。
