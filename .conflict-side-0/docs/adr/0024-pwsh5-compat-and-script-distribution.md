# PowerShell 5.1 兼容 + 安装脚本随发布包分发

Windows PowerShell 脚本头部统一加 #Requires -Version 5.1 + #Requires -PSEdition Desktop,Core。Remove-Service 在 PowerShell 7+ (Core) 直接用，Desktop (5.1) 兜底用 sc.exe delete。发布脚本 publish-app.ps1/publish.sh 生成 release/<rid>/ 时按 RID 复制对应安装脚本到 release/<rid>/scripts/ 子目录，命名统一。

## Considered Options

- **只用 sc.exe**：兼容 5.1/7+ 但丢失 New-Service 描述字段等便利。被否决。
- **PowerShell 7 强依赖**：拒绝 5.1 用户，违反兼容性要求。被否决。
- **#Requires + Remove-Service 兜底 + 脚本复制（当前选择）**：零新增依赖，兼容双版本。

## Consequences

- install-service.ps1 加 #Requires 头部 + Remove-Service 双分支逻辑。
- publish-app.ps1/publish.sh 新增按 RID 复制脚本到 release/<rid>/scripts/ 的步骤。
- release.yml CI 同步新增此复制步骤。
