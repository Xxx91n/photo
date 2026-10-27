# 新 UI 手工冒烟

> 定位：新 UI 尚未进入发布链，也没有 headless UI 测试；本文件记录可重复的手工冒烟步骤与最近一次执行结果。
> 视觉验收按 #49 seam 4：锚点 + 冒烟，不建 CI 像素基线。

## 规则页（#56）

1. 每次从全新的私有 `config` 目录开始（删除 `rules.json` 或先点 `Reset`），否则上一次保存的值会让开关起始状态相反；
   启动本票构建的 `photoprivacy-ui`，点击导航 `Rules`。
2. 检查：五族 × 六开关全部可见；PDF/EPS 行显示 `Caution`；专家开关只切换提示条，六开关本身始终可点。
3. 点 JPEG 的 `Strip all`（默认 true → false）与 `EXIF`（默认 false → true），点 `Save`。
4. 期望 `<exe dir>/config/rules.json` 含 30 个 `{family}_{group}` 布尔键：
   `jpeg_strip_all=false`、`jpeg_strip_exif=true`，其余族保持编码默认值。
5. 关闭并重启 UI，再进 `Rules`：JPEG 两个开关保持第 3 步的值，且无加载告警、无“Unsaved changes”。
6. 旧 UI 互读：CI 的 `FormatRulesStoreTests.Load_Reads_Rules_Written_By_The_New_Ui`
   用同一份 golden fixture 走冻结的 `FormatRulesStore.Load()`。

最近一次执行（2026-10-27，#56 实现窗口）：

- 二进制：本票 `cargo build -p photoprivacy-ui` 产物复制到私有目录后运行
  （并行 worktree 共用 `ui/target`，直接跑共享产物会被其他窗口的构建覆盖）。
- 步骤 4：rules.json 30 键与预期一致；步骤 5：重启后界面回读一致。
- 限制：RDP/GPU 会话下 `PrintWindow` 对 flip-model 窗口只返回黑帧；本冒烟以真实点击 + 文件断言为准，
  截图只作辅助，不入库。

## 待补

- 完整 28 项 + 服务模式 8 项在新 UI 的对应关系尚未定形（#49 Not yet specified）。
