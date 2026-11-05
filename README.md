# Bluefors Monitor

使用 Avalonia 与 ScottPlot 的 Bluefors 日志桌面查看器。默认主题跟随系统，数据入口只有 Logfiles 目录；SQLite 由程序作为内部索引自动管理。项目不包含实验日志、数据库、账户信息或预编译运行时。

本项目为第三方社区工具，与 Bluefors 官方无隶属或背书关系。当前为预览版，用于日志观察与事件回看，不提供设备控制或安全联锁。事件阈值和通道定义来自特定使用场景，检测结果不等同于故障诊断。

## 功能

- 11 个温度、流量和压力通道，独立纵轴、通道显示开关和实时跟随。
- 增量读取日志；暂停跟随后仍采集，保持当前时间窗口。
- 升温事件列表、事件带、单击高亮和双击前后各 30 分钟回看。
- 长区间使用保留极值和断点的概览，光标独立查询原始记录。
- 中文日历、时间选择、深浅主题、PNG 导出和设置保存。

## 开发

需要 .NET 10 SDK。支持目标为 Windows x64、macOS Apple Silicon；用户已在一台 Windows 电脑上成功启动并打开 Logfiles 目录；实时日志持续追加环境尚未实测。

```sh
dotnet restore
dotnet build -c Release
dotnet run --project src/BlueforsMonitor.Desktop
```

启动后选择 Logfiles 根目录。未选择目录时为空界面，之后恢复上次目录。日志时间按 UTC+08:00 解析和显示。

## 验证

```sh
dotnet run --project tests/BlueforsMonitor.Tests -c Release
```

这是不依赖测试框架的回归执行程序，通过返回 0，失败返回 1；不是 `dotnet test` 项目。测试自动创建并清理临时日志，覆盖追加、半行、BOM/CRLF、坏行、重复来源、截断、事件更新、中断恢复、重启与概览极值。原始 Python 数据对照不作为公开仓库的测试依赖。

界面验收见 [验收清单](docs/acceptance.md)。GitHub Actions 配置在三个系统运行构建和核心回归；最新结果以 Actions 页面为准。

## 发布

```sh
bash scripts/publish.sh osx-arm64
```

macOS 输出 `artifacts/Bluefors Monitor.app`。Windows PowerShell 执行：

```powershell
./scripts/publish.ps1
```

Windows 输出 `artifacts/BlueforsMonitor-win-x64.zip`。发布包包含 .NET 运行时。运行时补丁由安装的 SDK 选择；跨机器发布时记录 SDK 版本。脚本不执行签名、公证或自动上传。

## 结构与约定

- `src/BlueforsMonitor.Core`：领域模型、检测算法、日志解析、索引、历史读取、设置；不依赖界面框架。
- `src/BlueforsMonitor.Desktop`：启动、视图、界面状态与绘图控制器。
- `tests/BlueforsMonitor.Tests`：独立、无实验数据依赖的回归执行程序。
- `docs`：架构边界、交互验收和重构范围。

详见 [架构说明](docs/architecture.md)。使用 `.editorconfig`，提交前执行 `dotnet format whitespace`。行为变更与结构重构分开提交。

## 获取源码

```sh
git clone https://github.com/kahojyun/BlueforsMonitor.git
cd BlueforsMonitor
```

构建和测试状态见 [GitHub Actions](https://github.com/kahojyun/BlueforsMonitor/actions)。

本项目使用 [MIT License](LICENSE)，版权署名为 Bluefors Monitor contributors。第三方依赖及本机组件的许可见 [第三方声明](THIRD_PARTY_NOTICES.md)。

## 依赖更新

当前直接依赖：Avalonia 12.1.5、ScottPlot.Avalonia 5.1.59、Microsoft.Data.Sqlite 10.0.14。ScottPlot.Avalonia 5.1.59 要求 Avalonia 12。

提交的 `packages.lock.json` 固定各平台依赖图。CI 使用 `dotnet restore --locked-mode`，随后运行 `python scripts/check_dependencies.py` 比较锁文件与人工审核的 `licenses/inventory.json`，并校验许可证文件 SHA-256。声明仍需人工审核；检查程序负责发现依赖版本或许可原文漂移，不自动判断许可合规。

升级时先修改 PackageReference，执行 `dotnet restore --force-evaluate`，核对包许可证并同步第三方声明、原文与 inventory，再运行回归及发布检查。不要仅重算哈希来绕过审核。

桌面兼容性检查（需要图形桌面，会短暂打开窗口）：

```sh
dotnet run --project tests/BlueforsMonitor.DesktopChecks -c Release
```

该程序使用独立临时设置和合成日志，覆盖主题、日历、纵轴空间、事件定位及追加日志后的跟随行为；CI 仅编译此程序，核心回归在三个平台执行。
