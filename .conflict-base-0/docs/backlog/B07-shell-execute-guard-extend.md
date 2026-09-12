# B07 — `UseShellExecute = needElevation` 守卫盲区扩展

- **优先级**: 中　**来源**: report-02 偏差 2

## 问题

票 02 的 `No_UseShellExecute_True_In_Ui_Source` 只锁字面量 `UseShellExecute = true`；`ServiceManager.cs:373` 的 `UseShellExecute = needElevation`（UAC 提权设计，CONTEXT.md「Elevation Verb」）处于守卫盲区——合规但未被锁定，未来若有人误改 Ui 侧同类代码不会被拦。

## 验收

1. 扩展守卫：src/PhotoPrivacy.Ui 内 `UseShellExecute =` 赋值仅允许白名单形态（false / 提权变量且在 ServiceManager 内）。
2. 白名单外的 `UseShellExecute != false` 一律红灯。
3. 守卫测试绿。
