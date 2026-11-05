# 0.6.0 正式版准备

当前仍为待实机验收候选版，不自动正式发布。

## 兼容分支清单与决定

| 项目 | 处理及理由 |
|---|---|
| BlueforsAvalonia 命名空间和程序集 | 统一为 BlueforsMonitor，不再用 UI 框架区分产品 |
| 窗口、macOS 应用显示名 | Bluefors Monitor，窗口附产品版本 |
| macOS bundle ID | 固定为 io.github.kahojyun.blueforsmonitor，后续版本保持稳定 |
| 预览版设置和缓存目录 | 不扫描、不迁移、不删除；使用新的 BlueforsMonitor 根目录 |
| Python files 索引表及兼容写入 | 删除；source_files 是唯一文件偏移来源 |
| 旧 rowid 表到 WITHOUT ROWID 的迁移 | 删除；新建索引直接使用紧凑表 |
| Compact 操作 | 保留 VACUUM、optimize、checkpoint，供长期监测释放空闲空间 |
| 事件 JSON 大小写兼容、旧 Detection/DetectorSha256 字段 | 删除；使用显式版本的当前格式 |
| Python 结果目录对照测试入口 | 移除，保留事件算法回归及历史验证记录 |
| 设置缺字段、无效布局、损坏文件 | 保留当前格式的容错；缺失或未知版本回到默认设置 |
| 概览缺失后的原始数据回退 | 保留，处理不完整缓存，并非只服务旧版迁移 |
| 日志截断、重复来源、半行、无效样本、pending-refresh 恢复 | 保留，属于持续监测必需行为 |
| 事件算法、概览压缩结构 | 保持已验证的行为，不为更名而重写 |

## 存储约定

根目录为 .NET `LocalApplicationData` 下的 `BlueforsMonitor`。Windows 通常为 `%LOCALAPPDATA%\BlueforsMonitor`；macOS 为 `~/Library/Application Support/BlueforsMonitor`（以系统返回路径为准）。

- `settings.json`：必需 `Version: 1`。未知/缺失版本及损坏文件使用默认设置。
- `datasets/v1/<日志绝对路径哈希>/history.sqlite`：SQLite `PRAGMA user_version=1`；非空未版本化数据库和未知版本明确拒绝，不原地升级或删除。
- 同目录 `events.json`：`FormatVersion: 1`，保存 Source、TimeZone、Bounds、Counts、Events；未知版本拒绝。
- 当前概览沿用 zlib 压缩的两级 `[points,gaps]` 数据，属于 v1 索引格式。

第一次启动新版时重新选择 **原始 Logfiles**，程序建立新的本机索引。预览版设置、勾选通道和布局不会迁入，需要重新设置。旧应用退出后，可以自行清理旧 `BlueforsMonitor.Avalonia` 目录；不要把旧数据库复制到新目录，不要删除原始 Logfiles。

## 发布约定

默认脚本和 Draft release 工作流生成 Native AOT 标准包，无 `-aot` 后缀。JIT 诊断包必须显式指定 `--deployment jit`，带 `-jit` 后缀，草稿汇总拒绝 JIT 包。开发时 `dotnet run` 仍使用常规 .NET，保留调试体验。

更名后应测试首次启动、重新索引、退出恢复、实时追加、事件导航、主题、日期范围、手动缩放、PNG 和移动解压目录后启动。macOS 保留 ad-hoc 签名，但仍未进行 Developer ID 签名或公证。
