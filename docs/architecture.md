# 架构

依赖方向：Desktop → Core，Tests → Core。Core 的唯一外部依赖为 Microsoft.Data.Sqlite。

Domain 保存 Sample、EventRecord、Snapshot、WindowData、Clock 与检测算法。Data/Logs 管理文件读取、增量位置、文件替换和索引更新；Data/Storage 管理 SQLite 查询、概览和光标读取。Features/Settings 保存用户偏好。

Desktop 的 MonitorViewModel 通过属性通知提供状态、来源及事件数量。Views/Controls 提供独立界面控件，主窗口负责原生目录选择与会话协调；PlotController 负责曲线构建、断点和各曲线的独立轴绑定；PlotOverlays 负责光标与事件带。

这次采用渐进重构：主窗口仍协调异步请求、坐标轴手势和计时器，尚未将所有状态转换为命令式 MVVM。没有把 partial 文件拆分当作完整解耦。后续可继续抽取监测会话与坐标轴控制器，避免一次替换全部交互。

约束：后台查询捕获数据源与 revision；取消或过期结果不能覆盖新窗口。日志检测使用原始样本，显示概览不用于检测。应用正常启动不读取测试数据；测试与生产入口分离。设置保留既有路径与 JSON 格式，避免升级失去用户偏好。

重构保持算法和内部缓存结构不变；修复空界面移动光标时尝试查询不存在数据库的问题。

## 界面控件边界

- `DateRangeControl`：拥有起止编辑器、区间校验和错误提示；`SetRange` 只更新输入，用户点击应用且校验通过后才发出 `RangeApplied`。
- `ChannelPanelControl`：拥有通道勾选、读数卡片、自动/手动标记、选中纵轴与多纵轴选项。`Configure` 静默初始化；用户操作通过 `VisibilityChanged`、`AxisOptionsChanged` 通知主窗口；读数和手动状态由主窗口传入。
- `EventListControl`：内部使用 Avalonia 12.1 内置 TableView，拥有七列定义、单元格模板、当前范围和事件详情。列宽由 TableView 统一管理，支持拖动调整，无额外依赖。`SetEvents`、`Select` 用于静默同步数据及选择；用户选择、双击分别发出 `SelectionRequested`、`ReplayRequested`，避免程序同步反向触发回看。事件数量继续使用编译绑定。

三个控件不持有 MainWindow、DataStore 实例、AppSettings 或绘图对象，也不启动后台任务。主窗口保存设置、加载数据、控制坐标轴，并将结果送入控件。`ViewElements` 仅复用文本、按钮与网格放置的小型构建方法。桌面回归从视觉树定位控件，使用公开接口与按钮/列表事件验证行为；数据源和绘图协调的测试暂仍使用反射访问主窗口。
