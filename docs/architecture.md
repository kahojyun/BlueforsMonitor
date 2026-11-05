# 架构

依赖方向：Desktop → Core，Tests → Core。Core 的唯一外部依赖为 Microsoft.Data.Sqlite。

Domain 保存 Sample、EventRecord、Snapshot、WindowData、Clock 与检测算法。Data/Logs 管理文件读取、增量位置、文件替换和索引更新；Data/Storage 管理 SQLite 查询、概览和光标读取。Features/Settings 保存用户偏好。

Desktop 的 MonitorViewModel 通过属性通知提供状态、来源及事件数量。Views 负责控件和原生目录选择；PlotController 负责曲线构建、断点和各曲线的独立轴绑定；PlotOverlays 负责光标与事件带。

这次采用渐进重构：主窗口仍协调异步请求、坐标轴手势和计时器，尚未将所有状态转换为命令式 MVVM。没有把 partial 文件拆分当作完整解耦。后续可继续抽取监测会话与坐标轴控制器，避免一次替换全部交互。

约束：后台查询捕获数据源与 revision；取消或过期结果不能覆盖新窗口。日志检测使用原始样本，显示概览不用于检测。应用正常启动不读取测试数据；测试与生产入口分离。设置保留既有路径与 JSON 格式，避免升级失去用户偏好。

重构保持算法和内部缓存结构不变；修复空界面移动光标时尝试查询不存在数据库的问题。
