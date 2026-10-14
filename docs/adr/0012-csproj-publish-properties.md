> **修订 (ADR 0035)**: 实际实现已改为发布脚本 /p: 传递，而非 csproj PropertyGroup。SelfContained=true 在无 RID 的 dotnet build 下破坏输出路径，故未下沉到 csproj。发布脚本（publish-app.ps1 + publish.sh）是发布属性的 single source of truth。
>
> **修订（2026-10-14 治理清理注）**: correctness-round 票 06 已将 UI.csproj 残留的四枚发布属性移除并上提至双脚本 publish 调用，`PublishPropertiesCsprojGuardTests` 双向钉死（csproj 零发布属性 ∧ 每次 publish 必带三标志）。本 ADR 原始主张仅作历史记录。

# 发布属性写入 csproj PropertyGroup

SelfContained、PublishSingleFile、IncludeNativeLibrariesForSelfExtract、PublishTrimmed=false、DebugType=embedded 等发布属性写入 csproj 的 PropertyGroup，而非在命令行 -p 传递。降低脚本复杂度，确保本地和 CI 构建一致。

## Considered Options

- **方案 A（采纳）**: 属性入 csproj PropertyGroup
- **方案 B（否决）**: 属性在脚本命令行 -p 传递——脚本冗长，本地和 CI 容易不一致
