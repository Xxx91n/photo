# B06 — source-lint 测试 helper 去重

- **优先级**: 低　**来源**: report-02 code-review 判断题

## 问题

多套 source-lint guard（UiLauncherSourceTests / DesignSystemTests / WorkerProcessManagerTests 等）各自复制了"读源码文件 + 剥注释"helper，与 HardcodedChineseScanTests 同形。

## 验收

1. 提取共享 helper（如 `SourceLint.ReadStripped(path)`）到测试工程公共处。
2. 各 guard 改调共享 helper，断言语义零变化。
3. 全部 source-lint 测试绿。
