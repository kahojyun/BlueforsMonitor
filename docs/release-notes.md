本草稿用于正式发布前的实机验收，尚未正式发布。

本轮内容：
- 默认发布切换为 Native AOT；JIT 保留为开发与诊断对照。
- 产品、程序集、可执行文件统一为 Bluefors Monitor / BlueforsMonitor；窗口去掉 Avalonia 后缀。
- macOS bundle ID 固定为 io.github.kahojyun.blueforsmonitor。
- 使用新的 BlueforsMonitor 设置目录及 v1 索引，删除预览版/Python 专用迁移与兼容写入。
- 保留实时日志容错、概览恢复、索引压缩及现有绘图交互。

升级说明：首次打开请重新选择原始 Logfiles 建立索引，并重新设置主题、通道和布局。旧 BlueforsMonitor.Avalonia 数据不会自动迁移或删除。不要把旧数据库复制进新目录，不要删除原始日志。

Windows 完整解压后运行根目录 BlueforsMonitor.exe；macOS 打开 Bluefors Monitor.app。macOS 包已 ad-hoc 签名，尚未进行 Developer ID 签名或公证，首次下载可能需在系统设置中允许打开。

SHA256SUMS 提供下载校验，BUILD-INFO.json 记录源码提交、AOT 部署方式和构建环境。

验收重点：首次重新索引与出图、退出重开、真实日志追加与暂停/跟随、事件表、中文、mK/K、R/A、日期、手动缩放与 PNG 导出；Windows 移动解压目录后启动。

待实机验收后，由维护者手动决定正式发布。工作流只创建草稿，不覆盖已有版本。
