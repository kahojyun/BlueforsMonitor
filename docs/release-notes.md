本草稿用于实机验收，尚未正式发布。工作流不会自动发布或覆盖已有版本。

本轮内容：序列化改为 AOT 兼容实现，保留旧设置、事件和概览格式；增加独立 Native AOT/JIT 对照构建及实际发布包功能、启动、内存和体积检查。默认 Release 继续采用 JIT。

下载对应平台的 ZIP 并完整解压：
- Windows x64：双击根目录的 BlueforsMonitor.exe，保留 app 文件夹。
- macOS Apple Silicon：打开 Bluefors Monitor.app。本包已进行 ad-hoc 签名，尚未进行 Developer ID 签名和公证。首次打开若被 Gatekeeper 拦截，请在“系统设置 → 隐私与安全性”中允许打开，再启动应用；ad-hoc 签名不等于通过 Gatekeeper 公证。
- SHA256SUMS 提供文件校验，BUILD-INFO.json 记录源码提交、SDK、运行时和构建环境。

正式发布前请确认：
- 首次启动、旧设置/索引恢复、收起日期时首次出图。
- 真实日志追加、暂停/跟随、退出重开。
- 中文、mK/K、R/A、自动轴与手动缩放、PNG 导出。
- Windows 移动解压目录后启动；macOS 解压后的应用启动。

测试通过后，可手动决定发布为预发行版或正式版，并补充实机验证结果。若发现问题，保留本草稿，修改代码并递增 VERSION 后生成新的候选包。
