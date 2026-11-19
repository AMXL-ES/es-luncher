# DECISIONS · 默认值（可推翻）

> 这些是"我替你定的小事"，按 `AGENTS.md` §5 记录在此，不打断你。
> 每条都写了**推翻要改什么**，改起来都是分钟级。

| 日期 | 决策 | 理由 | 推翻要改什么 |
|---|---|---|---|
| 2026-11-19 | git 仓库本地身份：`HP <hp@mc-launcher.local>` | 全局 `user.name/email` 为空，不擅自改全局配置 | `git config --local user.email <真实>`；历史仅 1 个提交，改写代价极低 |
| 2026-11-19 | 所有 `.ps1` 用 UTF-8 **带 BOM** | 本机只有 Windows PowerShell 5.1，无 BOM 会把中文注释读坏 | 装 pwsh 7 后可去 BOM，同步 `.editorconfig` 与 `docs/` 说明 |
| 2026-11-19 | 脚本按 PS **5.1** 兼容写（不用 `::new()`、不依赖 7.x 语法） | 同上，`pwsh` 不存在 | 装 pwsh 7 后可放宽语法；脚本头 `#requires` 一并改 |
| 2026-11-19 | M0 构建为 framework-dependent（非单文件、不裁剪） | 先让冒烟跑通，减少变量；体积问题留 M10 | M10 分发时改 self-contained + trimming/AOT |
| 2026-11-19 | 中央包管理 `Directory.Packages.props` | 长项目版本漂移是主要腐化源 | 删 CPM 与 `PackageVersion` 项，改回 csproj 内联版本 |
| 2026-11-19 | 解决方案文件用 .NET 10 默认的 `GLauncher.slnx` | `dotnet new sln` 在 SDK 10 的默认产物 | 改回 `.sln`：`dotnet new sln --format sln` |
| 2026-11-19 | M2 的真实启动验收用 **1.18.2**（要求 Java 17，本机有 17.0.12） | 本机没有 Java 8，1.7.10 无法起；1.18.2 资产 447 MiB 在当前授权线内，且与现代参数格式一致 | 换目标版本只改命令行参数；M4 解锁 Java 8 后补验 1.7.10 |
| 2026-11-19 | 离线占位符默认值：`clientid`/`auth_xuid` 空串、`user_properties` = `{}` | 真实 version.json 会引用这三个；缺失会抛占位符异常，空串是官方离线启动的通行做法 | 接正版时由 token 流程填真实值 |
| 2026-11-19 | classpath 不检查文件存在性（纯组装）；存在性检查放在 CLI 启动前 | 引擎保持可单测、无 IO；CLI 负责给人看的"缺几个文件" | 需要引擎自检时再加 `Verify()` |
