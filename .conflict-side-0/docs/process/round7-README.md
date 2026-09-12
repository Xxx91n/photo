# Round 7 README — 第七轮收口摘要（2026-09-07）

权威全文见 .scratch/architecture-recovery/README.md（归档副本 _archive/architecture-recovery-round7-2026-09-07/README.md）。

## 状态表（终态）

| 票 | 标题 | Blocked by | 波次 | commit（origin 实物） | 最终验收 run |
|---|---|---|---|---|---|
| 28 | CI/CD 修复：push 测试门禁 + workflow 拆分 | None | 1 | e356db0 | 33945684509 SUCCESS（183+273） |
| 29 | 组合根 + DI 容器骨架 | 28 | 2 | 2eb5b8d | 33959142272 SUCCESS（191+305） |
| 30 | SettingsService 单一真相源 + code-behind 镜像收敛 | 29 | 3 | 5c74a2a | 33968573200 SUCCESS（191+311） |
| 31 | 服务轮询下沉 IHostedService | 30 | 4 | 8685211 | 34071736168 SUCCESS（191+317） |
| 32 | UI 手工探活冒烟 | 28, 31 | 5 | 7a1d6c4 | 清单交付口径（28 项未探活零虚构，待用户） |

## 合并拓扑

main(53c669e) ← 406603e(28) ← f1c8ec1(29) ← f78a693(30) ← 0b51f86(31) ← 3d46d2b(32 docs)。

## 复核与返修

- 28：❌→返修×3→✅（46 红灯四组根因 + 死过滤转活 + 2 flaky）；V1 窗口自行推送、V2 未停手移交（呈报未追认）。
- 29：✅（一次过；大脑动栈修复锚定缺陷——29 原锚 24 树无 ci.yml；but pull 两度误删由 undo 完整恢复）。
- 30：❌→返修×1→✅（守卫被考古注释击穿，ReadStripped 化）；窗口违规 0。
- 31：❌→返修×1→✅（CS0029 lambda `_` 吃 discard）；返修窗主动修正复核机制推测（质检复核要求的正确执行样本）。
- 32：✅ 清单交付口径（28 项未探活零虚构；运行侧验证悬置）。

## Backlog（待用户裁定是否立票）

1. 运行侧探活 28 项（需用户真实桌面 + release-readiness.ps1 新鲜产物）
2. 票 26/29/31 报告哈希与行数口径勘误
3. CONTEXT.md 术语补全（Composition Root / WindowPollingHostedService / CI Gate）
4. Ursa Toast 反馈补缺
5. 静态单例逐票收敛（App.RuntimeOptions / UiDiagnosticLog / MainWindow 16 处 Instance）
6. 全量 DI 迁移（TrayHost/AuditTailService/ConfigFileWatcher/ServiceModeController）
7. Smoke 测试 DLL-first 探测 Debug 产物
8. i18n 下拉文案刷新 hack VM 选项集合化

## 时效纠错

- MainWindow.axaml.cs：1433（round6 收口口径）→ 1158（票 30 后）→ 1057（票 31 后）。
- 按钮内联 FontSize=0 / 23 Button Selector 维持票 26 达成态。
