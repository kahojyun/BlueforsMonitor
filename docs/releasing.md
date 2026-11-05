# 发布流程

## 版本来源

根目录 `VERSION` 是产品版本的唯一来源，格式为 `X.Y.Z`。MSBuild、窗口标题、macOS bundle 和 ZIP 文件名均从它派生。升级时只修改此文件，不手改生成文件。`global.json` 固定构建 SDK，NuGet 锁文件固定依赖；升级 SDK 应作为独立变更验证。

## 生成待验收草稿

1. 更新 `VERSION` 和 `docs/release-notes.md`，提交并推送到 main。
2. 在 GitHub Actions 选择 **Draft release → Run workflow → main**。也支持推送与 VERSION 一致的 `vX.Y.Z` 标签。
3. 工作流先在 Windows、macOS、Linux 构建和回归，再分别在 Windows x64、macOS arm64 runner 打包。
4. 校验两个包来自同一干净提交、版本和可执行文件架构正确，生成 `BUILD-INFO.json` 与 `SHA256SUMS`，最后创建 **Draft + Pre-release**。

普通 CI 不创建 Release。发布工作流不包含正式发布步骤。草稿只对有权限的仓库成员可见；下载后按 Release 中的清单实机测试。通过后由维护者在 GitHub 手动发布，并根据成熟度决定是否保留 Pre-release 标记。

同版本的已发布 Release 不会被覆盖。已有草稿内容不同也会停止，避免替换正在测试的包。修改代码后应增加版本号生成下一候选版；若只是重试失败任务，优先使用 GitHub 的 Re-run failed jobs 复用已成功的包。

## 产物与校验

- `BlueforsMonitor-X.Y.Z-win-x64.zip`：完整解压后运行根目录的 `BlueforsMonitor.exe`。
- `BlueforsMonitor-X.Y.Z-osx-arm64.zip`：包含 macOS app bundle。
- `BUILD-INFO.json`：源码提交、SDK、运行时、构建平台及 workflow run；每个平台包内也保留自己的构建信息。
- `SHA256SUMS`：两个 ZIP 和构建信息的 SHA-256。

macOS/Linux 在下载目录运行 `shasum -a 256 -c SHA256SUMS`。Windows 可用 `Get-FileHash 文件名 -Algorithm SHA256` 与清单逐项对照。校验值用于确认下载完整性；macOS 包已进行 ad-hoc 签名，但未进行 Developer ID 签名或公证；首次下载仍可能需要在系统设置中允许打开。Windows 包未进行代码签名。

## 本地打包

安装 `global.json` 指定的 SDK 和 Python 3，运行 `bash scripts/publish.sh osx-arm64` 或 Windows PowerShell 的 `./scripts/publish.ps1`。输出位于 `artifacts/release/`，文件名包含版本；包自带 .NET 运行时。

Windows 启动器需要 Zig，或已初始化的 VS x64 开发者环境中的 cl.exe。macOS bundle 必须在 macOS 上生成。脚本允许本地未提交构建并标记 dirty，但草稿流程拒绝这种包。发布校验测试：`python -m unittest discover -s tests/release -v`。

macOS 打包会按内部 Mach-O 文件、外层 app 的顺序执行 ad-hoc 签名，再运行严格签名验证；生成 ZIP 后重新解压，再验证全部原生文件及 app 的签名，并启动解压后的程序运行 10 秒，检测启动时崩溃或运行库缺失。任一步失败都会阻止上传。此流程不启用 Hardened Runtime；未来采用 Developer ID/公证时需单独配置 .NET JIT 权限并验证。
