# Bluefors Monitor

使用 Avalonia 与 ScottPlot 的 Bluefors 日志桌面查看器。默认主题跟随系统，数据入口只有 Logfiles 目录；SQLite 由程序作为内部索引自动管理。项目不包含实验日志、数据库、账户信息或预编译运行时。

本项目为第三方社区工具，与 Bluefors 官方无隶属或背书关系。当前为预览版，用于日志观察与事件回看，不提供设备控制或安全联锁。事件阈值和通道定义来自特定使用场景，检测结果不等同于故障诊断。

## 功能

- 11 个温度、流量和压力通道，独立纵轴、通道显示开关和实时跟随。
- 温度纵轴和光标读数自动切换 mK/K，保留手动缩放；事件表统一使用 mK。
- 增量读取日志；暂停跟随后仍采集，保持当前时间窗口。
- 升温事件列表、事件带、单击高亮和双击前后各 30 分钟回看。
- 长区间使用保留极值和断点的概览，光标独立查询原始记录。
- 中文日历、时间选择、深浅主题、PNG 导出和设置保存。
- 固定位置的通道开关、独立读数栏、可拖动/收起的事件表、专注曲线模式与布局状态恢复。
- 缓存优先显示、后台检查日志、自动纵轴、R/A 快捷操作、顶部通道标签和索引压缩。

## 开发

需要 `global.json` 指定的 .NET SDK。支持目标为 Windows x64、macOS Apple Silicon；用户已在一台 Windows 电脑上成功启动并打开 Logfiles 目录；实时日志持续追加环境尚未实测。

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

macOS 输出 `artifacts/release/BlueforsMonitor-<版本>-osx-arm64.zip`。Windows PowerShell 执行：

```powershell
./scripts/publish.ps1
```

Windows 打包还需要 Python 3，以及 Zig 或 VS x64 开发者命令行中的 cl.exe，用于编译无控制台的原生启动器。输出 `artifacts/release/BlueforsMonitor-<版本>-win-x64.zip`；完整解压后双击根目录唯一的 `BlueforsMonitor.exe`，依赖位于 `app` 子目录。包包含 .NET 运行时。

产品版本统一来自 `VERSION`。独立的 **Draft release** 工作流生成两平台包、构建信息和 SHA-256 校验清单，仅创建 Release 草稿，实机验收后由维护者正式发布。详见 [发布流程](docs/releasing.md)。

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

## 窗口布局

点击「自定义时间」展开日期输入；「专注曲线」暂时隐藏日期、事件表和详情，保留通道读数、事件带和主要操作，点击「退出专注」恢复之前的面板状态。

拖动绘图区与事件表之间的分隔条调整高度；点击「升温事件」收起或展开列表。选中事件后可用「详情」开关查看说明。日期展开、事件展开、详情开关、事件面板高度和专注模式自动保存在原有设置文件中，重启后恢复。窗口较小时会临时限制事件区高度，避免挤掉绘图区，不覆盖保存的高度。

主题、全部历史、通道重新排列和导出等位于「更多」。完整目录/状态信息可悬停查看，底部「操作帮助」提供手势提示。修改布局不会更改跟随状态、时间范围或手动纵轴范围。

## 温度显示单位

每个温度通道按当前可见时间范围内样本的最大绝对值独立选择单位，底层数据保持 K。首次有数据时，低于 1 K 显示 mK，否则显示 K；之后达到 1 K 切到 K，降到 0.9 K 以下才切回 mK，0.9–1 K 之间保留原单位，避免反复跳动。没有可见样本时保留上次单位，切换日志目录后重新判断。

曲线、纵轴刻度/标签、光标读数和 PNG 导出使用一致的单位。单位变化只换算显示数值，保持手动纵轴对应的实际温度范围；压力、流量与事件表的 mK 列不变。

## 常用曲线操作与缓存

- **R / 重排按钮 / 图内双击**：按当前时间范围重排所有可见曲线，并恢复自动模式。
- **A / 适配按钮 / 纵轴或顶部标签双击**：适配选中通道。文本、日期编辑器内的按键保留输入含义。
- **自动轴**：随当前视野适配未手动调整的通道；手动平移、缩放的轴保持范围。该偏好会保存。
- 重开目录时先显示已有缓存，再检查日志变化。首次建立索引仍需要读取日志、生成概览与事件，进度文字会区分这些阶段。
- 新索引采用紧凑表结构；旧索引可运行一次 **更多 → 压缩本机索引**。压缩在后台执行，需要额外临时磁盘空间，不改动源日志。
- 同步结束后尝试 checkpoint 并收缩 WAL；其他进程仍在读取时可能暂时保留，之后重试。不要手动删除 WAL。来源记录用于重复文件和截断恢复，因此索引仍可能大于旧 Python 版。
