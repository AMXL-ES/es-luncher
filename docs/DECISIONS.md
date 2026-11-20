# DECISIONS · 默认值（可推翻）

> 这些是"我替你定的小事"，按 `AGENTS.md` §5 记录在此，不打断你。
> 每条都写了**推翻要改什么**，改起来都是分钟级。

| 日期 | 决策 | 理由 | 推翻要改什么 |
|---|---|---|---|
| 2026-11-19 | git 仓库本地身份：`HP <hp@es-luncher.local>` | 全局 `user.name/email` 为空，不擅自改全局配置 | `git config --local user.email <真实>`；历史仅 1 个提交，改写代价极低 |
| 2026-11-19 | 所有 `.ps1` 用 UTF-8 **带 BOM** | 本机只有 Windows PowerShell 5.1，无 BOM 会把中文注释读坏 | 装 pwsh 7 后可去 BOM，同步 `.editorconfig` 与 `docs/` 说明 |
| 2026-11-19 | 脚本按 PS **5.1** 兼容写（不用 `::new()`、不依赖 7.x 语法） | 同上，`pwsh` 不存在 | 装 pwsh 7 后可放宽语法；脚本头 `#requires` 一并改 |
| 2026-11-19 | M0 构建为 framework-dependent（非单文件、不裁剪） | 先让冒烟跑通，减少变量；体积问题留 M10 | M10 分发时改 self-contained + trimming/AOT |
| 2026-11-19 | 中央包管理 `Directory.Packages.props` | 长项目版本漂移是主要腐化源 | 删 CPM 与 `PackageVersion` 项，改回 csproj 内联版本 |
| 2026-11-19 | 解决方案文件用 .NET 10 默认的 `EsLuncher.slnx` | `dotnet new sln` 在 SDK 10 的默认产物 | 改回 `.sln`：`dotnet new sln --format sln` |
| 2026-11-19 | M2 的真实启动验收用 **1.18.2**（要求 Java 17，本机有 17.0.12） | 本机没有 Java 8，1.7.10 无法起；1.18.2 资产 447 MiB 在当前授权线内，且与现代参数格式一致 | 换目标版本只改命令行参数；M4 解锁 Java 8 后补验 1.7.10 |
| 2026-11-19 | 离线占位符默认值：`clientid`/`auth_xuid` 空串、`user_properties` = `{}` | 真实 version.json 会引用这三个；缺失会抛占位符异常，空串是官方离线启动的通行做法 | 接正版时由 token 流程填真实值 |
| 2026-11-19 | classpath 不检查文件存在性（纯组装）；存在性检查放在 CLI 启动前 | 引擎保持可单测、无 IO；CLI 负责给人看的"缺几个文件" | 需要引擎自检时再加 `Verify()` |
| 2026-11-19 | 凭据加密用 **DPAPI P/Invoke**（`crypt32.dll`），不引入 NuGet 包 | Windows 优先、零额外依赖；非 Windows 明确抛异常而不是降级明文 | 跨平台时换成 keyring 实现，接口 `ITokenStore` 不变 |
| 2026-11-19 | **不内置任何微软 client ID**；必须由用户在 Azure 注册后提供（`--client-id` 或 `ESLUNCHER_MSA_CLIENT_ID`） | 用别人的 client ID 属于违规，也违反"不伪造身份"红线 | 无——这是合规硬约束 |
| 2026-11-19 | 皮肤站认证按用户裁剪暂缓，仅留 `IAuthProvider` 扩展点 | 用户明确"先只做离线，给另外两个留位置" | 要启用时实现 `SkinStationAuthProvider` + authlib-injector 注入 |
| 2026-11-20 | 托管运行时（`D:\mc-data\java`）**优先于系统 JDK**（同主版本） | 玩家机器上的 JDK 可能被改坏/版本漂移；托管版与版本元数据严格对应 | 想优先系统 JDK 就把 `layout.JavaDir` 从 `Discover` 的显式根里去掉 |
| 2026-11-20 | 运行时只下载 `downloads.raw`，不使用 Mojang 的 lzma 包 | 少一层解压与校验，代价是下载量略大 | 想省流量时改用 lzma 流解压 + 同样的 SHA1 校验 |
| 2026-11-20 | 运行时按组件/版本多份并存，不自动清理旧版本 | 回滚最省事；磁盘问题留到 M10/M11 统一处理 | 加 LRU 清理策略，或在 `java install` 后删除同组件旧版本 |
| 2026-11-20 | 实例共享 `libraries/assets`，实例目录只放 `config.json` + `game/` + `natives/` | 省磁盘与下载；与主流启动器一致；并行实例只隔离游戏目录与 natives | 需要完全隔离时改成实例自带库目录，代价是磁盘翻倍 |
| 2026-11-20 | 实例删除必须 `--force` | 会连同 `game/`（世界存档）一起删，防手滑 | 无——这是安全默认值 |
| 2026-11-20 | 实例名允许中文与空格，禁止路径分隔符/Windows 非法字符/首尾空格 | 中文玩家体验优先；目录名即实例名，校验在入口做一次 | 需要跨文件系统最稳时改成 ASCII slug + 显示名分离 |
| 2026-11-20 | 启动日志名 = 实例名 + 版本 + 时间 + 4 位随机 | 同版本并行会撞秒级时间戳，实测导致文件锁冲突 | 无需改；若要固定命名可加文件锁重试 |
| 2026-11-20 | 产品更名 ES Luncher，数据根仍是 `D:\mc-data` | 复用已下载的 800+ MiB 资源与运行时，避免重下 | 想换数据根：`--data` 或 `ESLUNCHER_DATA`，再跑一次 fetch |
| 2026-11-20 | 加载器合并：父库在前、子库在后，主类子覆盖，参数父→子拼接 | 与官方启动器/Prism 一致；Fabric/Quilt/Forge 三者都用同一套 | 若某加载器要求自己的库在前，再为该加载器加特例 |
| 2026-11-20 | BootstrapLauncher（Forge/NeoForge 1.17+）从 classpath 排除原版 jar | 原版 jar 会与 srg 模块重复导出包，实测启动直接崩 | 老版本 Forge（≤1.16.5）不适用，按 mainClass 判断自动切换 |
| 2026-11-20 | Forge installer 前自动写最小 `launcher_profiles.json` | installer 强制要求存在该文件，否则退出 1 | 无——纯 stub，不影响官方启动器文件 |
