# 配置版本迁移：Math.Max + 默认值兜底 + 原子配置备份

保持当前 Math.Max(1, dto.SchemaVersion) 模式不变。DTO 用 init 默认值兜底（新字段不存在的旧配置自动用默认值）。config.json 修改前（如 ConfigEditor 保存）自动原子备份到 config.json.bak（temp + rename）。不做主动迁移写回——旧配置加载时用默认值填充新字段即可。升级 schema_version 时只在用户主动保存配置时写回新版本号。

## Considered Options

- **chain of migrators 模式**：每个版本一个 migrator，链式执行 vN→vN+1，迁移后验证。约 100+ 行代码。被否决——桌面工具不需要多租户 SaaS 级别的迁移复杂度。
