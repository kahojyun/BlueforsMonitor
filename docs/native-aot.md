# Native AOT 候选版

默认发布仍是自包含 JIT。AOT 是独立候选包，文件名以 `-aot.zip` 结尾；使用同一份日志、设置、SQLite 索引和事件格式。两种版本不应同时打开同一个缓存做实机测试。

## 构建与下载

GitHub Actions 中手动运行 **AOT comparison**。Windows x64 和 macOS arm64 各构建一组 JIT/AOT 包，在相同 runner 上测试。成功后下载对应的 `aot-comparison-*` artifact，其中包含两个 ZIP、SHA256SUMS、构建信息、comparison.json 和测试截图。此流程没有发布权限，不创建或修改正式 Release；artifact 保留 30 天。

本地在目标平台执行（Python 3、global.json 中的 SDK；Windows 需 VS x64 C++ 工具）：

```sh
python scripts/release.py build --runtime osx-arm64 --output artifacts/comparison
python scripts/release.py build --runtime osx-arm64 --deployment aot --output artifacts/comparison
python scripts/compare_packages.py --directory artifacts/comparison --runtime osx-arm64 --output artifacts/checks
```

Windows 改为 `win-x64`。默认锁文件与 `packages.aot.lock.json` 分开；AOT 编译器及裁剪工具也锁定版本并纳入第三方声明。AOT 发布将裁剪/AOT 警告视为错误，不全局屏蔽警告。

## 验证范围与数据兼容

设置和事件改为 System.Text.Json 源生成。概览写入使用 Utf8JsonWriter，保持既有 `[points, gaps]` 格式和 zlib 压缩。普通构建也关闭隐式 JSON 反射，避免只在 AOT 才暴露问题。回归包含旧设置、大小写不敏感的旧事件、两个概览层级和日志追加/截断/重启。

发布程序有显式诊断入口 `--package-check OUTPUT LOGFILES`：只在指定 OUTPUT 下保存设置和索引；会读取日志、生成/读取索引、导航事件、检查手动轴与日期范围、切换主题并输出 PNG 和报告，完成后退出。普通启动不进入此路径。比较脚本生成 14 天、80,640 条合成温度样本，并让两个程序交替使用同一份缓存以检查互通。

macOS 保留 ad-hoc 签名、解压后签名验证和启动检查。AOT 不代替 Developer ID 签名或公证。

## 如何解读 comparison.json

- 第 -1/0 轮分别由 AOT/JIT 重建缓存并交给另一种程序读取，不计入中位数。之后交替启动进程，各测三次。
- `window_opened_ms`：程序诊断计时开始到 Opened；不是操作系统进程创建到首帧的精确计时。
- `first_plot_rendered_ms`：同一起点到加载最近窗口并完成一次 ScottPlot 离屏渲染。不是显示器实际呈现时间。
- `working_set_bytes`：完成诊断操作后的进程工作集，不是峰值。
- `process_total_ms`：包含完整诊断和进程退出，不能当作启动时间。
- ZIP 和解压体积排除调试符号；同一提交、SDK、同一平台比较。

这些是暖操作系统缓存、合成数据的可重复对照。云端 runner 与实机配置不同；是否改为默认发布，仍需 Windows 实机检查首次打开真实大索引、持续日志追加、中文、PNG、缩放和长时间运行。现阶段不以 AOT 替代数据读取或绘图性能优化。
