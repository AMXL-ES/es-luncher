# Changelog

遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号用语义化版本。

## [0.1.5] — 2026-11-21

### 新增
- 自动更新：Velopack 接管安装、便携与增量更新；「其他 → 更新」可检查/下载/重启；增量包实测 87 KB（完整包 14 MB）。
- 关于页重做：运行环境、第三方组件（版本运行时读取）、数据与服务、鸣谢四张卡。
- 基准工具：`scripts/bench-startup.ps1` / `bench-memory.ps1` / `bench-download.ps1`，数字见 `docs/M11-performance.md`。
- 启动埋点：`ESLUNCHER_STARTUP_TRACE=1` 输出各阶段耗时。
- 实例页空态与「显示 N / M 个实例」计数。

### 变更
- 下载 / 设置 / 关于三页改为按需创建：冷启动中位 **1306 → 948 ms**，常驻内存 **124.6 → 112.1 MB**。
- 鸣谢名单去掉「开发者」字样，改为贡献描述；名单仍可在 `Assets/credits.json` 扩展。
- 实例页布局重做（行结构、卡片自适应、工具栏无障碍名称）。
- 分发改用 Velopack，Inno Setup 脚本退役（保留作历史参考）。

### 修复
- 实例页搜索行与列表用 margin 硬凑导致的错位。
- 启动时同步 DPAPI 解密拖慢首屏（改为后台填充）。
- 文档编码问题：`ISSUES.md` 由 GBK 转 UTF-8；`RELEASE-CHECK.md` 因双重乱码整篇重写。

## [0.1.0] — 2026-11-20

首个可预览版本：版本下载（官方 / BMCLAPI 回退）、Java 运行时管理、离线启动、实例系统、Fabric/Quilt/Forge 加载器、内容管理（mods/资源包/光影/世界/数据包/截图）、Modrinth 检索安装、`.mrpack` 导入导出、世界备份还原、诊断与诊断包、中英界面。

[0.1.5]: https://github.com/<owner>/es-luncher/compare/v0.1.0...v0.1.5
[0.1.0]: https://github.com/<owner>/es-luncher/releases/tag/v0.1.0
