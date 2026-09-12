# release/ 目录 .gitignore + CI artifact 上传

release/ 目录加入 .gitignore 不提交。CI 用 upload-artifact 上传构建产物，retention-days: 7 天自动过期。smoke test 先跑 dotnet test --filter "Category!=Smoke&Category!=ExifTool" 通过再进 release matrix，release 后做 test -f 结构验证。

## Considered Options

- **方案 A（采纳）**: .gitignore + upload-artifact retention 7 天
- **方案 B（否决）**: release/ 提交到 git——二进制文件不应入库
