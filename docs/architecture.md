# 架构

依赖方向：Desktop → Core，Tests → Core。Core 的唯一外部依赖为 Microsoft.Data.Sqlite。

Domain 保存 Sample、EventRecord、Snapshot、WindowData、Clock 与检测算法。Data/Logs 管理文件读取、增量位置、文件替换和索引更新；Data/Storage 管理 SQLite 查询、概览和光标读取。Features/Settings 保存用户偏好，Features/Monitoring 管理监测会话。

Desktop 的 MonitorViewModel 通过属性通知提供状态、来源及事件数量。Views/Controls 提供独立界面控件，主窗口负责原生目录选择、控件事件与界面协调；MonitoringSession 负责数据源和异步查询生命周期；AxisController 负责时间范围与各通道纵轴；PlotController 负责曲线构建、断点和各曲线的独立轴绑定；PlotOverlays 负责光标与事件带。

采用渐进重构：主窗口将指针事件换算为控制器调用，保留 DispatcherTimer（刷新 10 秒、视野防抖 130 ms、光标 50 ms）、跟随开关、事件选择、布局和用户提示。后台任务、缓存、取消源和纵轴状态已移至独立组件；没有把 partial 文件拆分当作完整解耦，也未强制把所有界面行为转换为命令式 MVVM。

约束：后台查询捕获数据源与 revision；取消或过期结果不能覆盖新窗口。日志检测使用原始样本，显示概览不用于检测。应用正常启动不读取测试数据；测试与生产入口分离。设置保留既有路径与 JSON 格式，避免升级失去用户偏好。

重构保持算法和内部缓存结构不变；修复空界面移动光标时尝试查询不存在数据库的问题。

## 界面控件边界

- `DateRangeControl`：拥有起止编辑器、区间校验和错误提示；`SetRange` 只更新输入，用户点击应用且校验通过后才发出 `RangeApplied`。
- `ChannelPanelControl`：拥有通道勾选、紧凑读数、自动/手动标记、选中纵轴与多纵轴选项。`Configure` 静默初始化；用户操作通过 `VisibilityChanged`、`AxisOptionsChanged` 通知主窗口；读数和手动状态由主窗口传入。
- `EventListControl`：内部使用 Avalonia 12.1 内置 TableView，拥有七列定义、单元格模板、当前范围和事件详情。列宽由 TableView 统一管理，支持拖动调整，无额外依赖。`SetEvents`、`Select` 用于静默同步数据及选择；用户选择、双击分别发出 `SelectionRequested`、`ReplayRequested`，避免程序同步反向触发回看。事件数量继续使用编译绑定。

三个控件不持有 MainWindow、DataStore 实例、AppSettings 或绘图对象，也不启动后台任务。主窗口保存设置、调用监测会话与坐标轴控制器，并将结果送入控件。`ViewElements` 仅复用文本、按钮与网格放置的小型构建方法。桌面回归从视觉树定位控件，使用公开接口与按钮/列表事件验证行为；窗口集成测试仍通过反射取得会话/控制器及触发窗口动作；组件自身通过公开方法独立验证。

## 布局状态

`LayoutSettings` 是现有 AppSettings 内的独立布局配置，旧配置缺少此字段时使用默认值，读取时校验事件面板高度。`MainWindow.Layout.cs` 仅协调面板可见性、分隔条、专注模式与保存；专注模式不会覆盖普通模式的日期/事件/详情偏好。窗口尺寸导致的临时高度约束不写回配置，用户拖动或键盘调整才保存面板高度。

通道面板将开关与光标读数合并为紧凑可换行项目；事件表移除固定 170 高度，由窗口分配空间，详情仅在选中事件且展开时显示，并限制高度以保留可用数据行。

## 监测会话

`MonitoringSession` 位于 Core，不依赖 Avalonia、ScottPlot 或设置单例。构造时传入初始 DataStore 和缓存目录；拥有当前 Store、显示缓存、revision、光标桶缓存、日志导入器及取消源。`OpenAsync` 成功后才替换数据源，失败时保留原源；`RefreshAsync` 串行化扫描，报告变化及脏区间；`LoadAsync` 返回当前显示结果与是否重新读取；`ReadCursorAsync` 提供原始样本，仍由界面格式化读数。新的视野请求即使命中缓存，也会取消之前的在途查询，防止晚到的旧视野覆盖缓存。

会话由单一拥有者上下文调用（桌面中为 UI 线程），I/O 在后台执行；它不是可从任意线程同时调用的服务。结果返回前检查取消、会话代次、源对象及 revision。过期结果与过期异常返回 null，当前错误交给界面展示；关闭窗口时 Dispose 取消工作，并阻止迟到的光标查询更新缓存。空日志目录保持监测，之后可接收新日志。

核心测试通过内部查询委托控制任务完成顺序，不依赖延时碰运气；生产构造使用真实 DataStore/LogImporter。缓存结构、检测算法和公开启动入口不变。

## 坐标轴控制器

`AxisController` 位于 Desktop/Plotting，仅持有 ScottPlot.Plot，不持有主窗口、控件、设置或监测会话。拥有时间范围、通道轴及手动模式集合，提供范围约束、轴命中、显隐同步、排列、平移、缩放、单轴适配、恢复自动及跟随时扩展自动轴。数据与可见通道由调用方传入；绘图刷新及通道面板标记由窗口同步。

保留原有排列/适配公式、隐藏轴移出布局、单/多轴选择和手动轴不被自动扩展覆盖的规则。独立控制器检查验证数值和状态，桌面回归继续验证实际布局及界面集成。

温度显示单位由 AxisController 按可见数据选择（首次阈值 1 K，后续上切 1 K、下切 0.9 K）；独立保存各通道的单位历史，源切换时重置判断。数据不变，显示倍率传给 PlotController 和光标格式化；单位切换同步换算纵轴上下限，保留手动标记。窗口在缓存命中的导航后也检查单位变化，拖动的初始轴范围按当前倍率换算。事件模型与表格继续固定 mK。
