# 本次验证记录

- macOS 上 Release 构建：零警告、零错误。
- 无实验数据依赖的回归执行程序通过：日志解析、追加、半行、坏行、重复来源、截断、恢复、设置和概览。
- 本地额外使用旧 Python 导出的结果核对：81 个事件全部一致。该数据不在仓库或源码 ZIP 中。
- Mac 自包含发布成功，桌面启动显示空状态、Logfiles 入口及跟随系统主题。
- Windows x64 自包含发布成功。用户报告已在一台 Windows 电脑上成功启动并打开 Logfiles 目录；尚未在实时日志持续更新环境测试，未声称所有 Windows 交互均已验收。
- GitHub CI 在推送和拉取请求时运行；最新结果见仓库 Actions 页面。

可选本地历史对照：`dotnet run --project tests/BlueforsMonitor.Tests -c Release -- --reference <包含 history.sqlite 与 events.json 的目录>`。此入口仅属于测试程序。
