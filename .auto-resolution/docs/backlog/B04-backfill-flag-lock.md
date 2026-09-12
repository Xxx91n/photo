# B04 — `_backfillSucceeded` 无锁 bool 收敛进 `_gate`

- **优先级**: 低　**来源**: report-05 遗留事项

## 问题

`AuditTailService._backfillSucceeded` 为无锁 bool，当前仅 backfill 循环单线程读写故安全；若未来引入多 fetcher 并发将成为竞争隐患。

## 验收

1. 该标志读写收进 `_gate` 锁内（或改 `Volatile`/`Interlocked` 并论证）。
2. 既有 backfill 9 测试保持绿。
