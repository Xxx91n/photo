# 服务操作后立即重试探测 + 指数退避

ServiceManager.Install/Uninstall/Start/Stop 返回成功后，调 IServiceStateProbe.GetState 用重试循环：for (int i = 0; i < 8; i++) { var state = _probe.GetState(name); if (state == desired) return state; await Task.Delay(100ms * 2^i, ct); }。总超时约 25 秒（100+200+...+12800ms）。UI 在等待时显示 BusyState，成功后更新 ViewModel。

## Considered Options

- **固定延迟 3 秒后探测一次**：慢且不灵活。被否决。
- **Bidirectional push 通知**：过度工程。被否决。
- **指数退避重试（当前选择）**：标准模式，零新增依赖。

## Consequences

- ServiceManager 或 ViewModel 新增 WaitForStateAsync(serviceName, desired, ct) 方法。
- UI 加 BusyState（IsBusy=true during wait）。
- 需测试验证重试序列和达到 desired 后停止。
