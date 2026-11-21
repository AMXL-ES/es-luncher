# PROGRESS · ES Luncher

> 唯一事实源：本文件回答「现在在哪」。历史细节进 `docs/`，决策进 `docs/adr/`，默认值进 `docs/DECISIONS.md`。
> **每轮开局三读**：本文件 → `docs/INDEX.md` → 当前里程碑文档。

- **目标 id**：`goal-db5cdd51-065f-4a37-a25d-883614f0ab16`
- **当前阶段**：**M11 性能与界面打磨**（2026-11-21）：冷启动 1306→948 ms、常驻 124.6→112.1 MB；关于页重做、实例页布局纠正；P1/P2 收口
- **项目根**：`D:\dev\es-luncher` ｜ **数据根**：`D:\mc-data` ｜ **不占 C 盘**
- **上游契约**：`C:\Users\HP\Desktop\Minecraft启动器-项目启动提示词.md`

## 已定（不可逆，已确认）

| 决策 | 值 |
|---|---|
| 技术栈 | .NET 10（SDK 10.0.401）+ Avalonia 12.1.6；引擎 `EsLuncher.Core` 零 UI 依赖 |
| 许可证 | **GPL-3.0-or-later**（现在公开）→ 可合法移植 Prism/MultiMC，须保留声明 |
| 平台 | Windows 优先，架构跨平台 |
| 认证 | **先只做离线**；为微软正版（M3b，阻塞于 Azure client ID）与皮肤站（M3c，authlib-injector）预留 Provider 接口 |
| 用户定位 | 专业玩家/开发者，信息密度优先，不做"简单模式" |
| 交付顺序 | 自底向上：先引擎（CLI 验收），M7 之前不写 UI 业务逻辑 |
| 下载源 | 官方优先 → 失败降级镜像 → 手动可切换 |
| 目录 | 工程 `D:\dev\es-luncher`，数据 `D:\mc-data` |

## M0 验收证据（2026-11-19）

| 判据 | 命令 | 结果 |
|---|---|---|
| 构建零警告 | `dotnet build EsLuncher.slnx -c Release` | **exit 0，0 warning / 0 error**；产物 `src/EsLuncher.App/bin/Release/net10.0/EsLuncher.exe`（0.2 MB，framework-dependent） |
| 空窗口能开能关 | `powershell -NoProfile -File scripts\smoke-m0.ps1` | **exit 0**；冷启动 **933 ms / 696 ms**（两次）；工作集 **102.3 / 99.0 MB**；`CloseMainWindow` 正常退出，exit=0 |
| 探活表成型 | `powershell -NoProfile -File scripts\probe-network.ps1 -JsonOut D:\mc-data\logs\net.json` | **8/8 符合预期**；官方 manifest 1.43 s、BMCLAPI 0.18 s、CurseForge 403（需 key） |
| 测试骨架可用 | `dotnet test EsLuncher.slnx -c Release` | exit 0；**2 passed / 0 failed**（含架构守卫 `Core_must_not_reference_any_ui_framework`） |
| 事实源 | —— | `PROGRESS.md` / `ROADMAP.md` / `AGENTS.md` / `docs/env-report.md` + ADR 0001/0002 |
| 版本库 | `git log --oneline` | 首提交 `chore: scaffold ES Luncher M0` |

## 已知环境限制（会一直影响后文）

1. 本机**只有 Windows PowerShell 5.1**（无 pwsh 7）→ `.ps1` 必须 UTF-8 **带 BOM**，且按 5.1 语法写。
2. PATH 上的 `java` 会崩（Oracle javapath，exit `0xC0000409`）→ **永远用绝对路径**；本机另有 JDK 17（`C:\Program Files\Microsoft\jdk-17.0.12.7-hotspot`）。
3. C 盘仅余 33.6 GB → 所有构建缓存/数据走 D。
4. `Avalonia.Diagnostics` 无 12.x 稳定版 → 暂不引入。

## M1 调研（2026-11-19，只读，未开工）

详见 `docs/M1-metadata-download.md`，要点：

- manifest **273 KB / 925 版本**（release 103、snapshot 761、old_beta 26、old_alpha 35），`latest.release=26.3`
- 现代版本 26.3 完整下载量 ≈ **587 MiB**（client 39.6 + libs 111 + assets 465），资源对象 **5,149 个**（索引文件本身 597,235 B）
- 26.3 **已无 natives classifiers**，1.7.10 仍有 → 新旧两套路径都要实现
- `javaVersion.majorVersion` 在 legacy 版本里也有（1.7.10 → 8）→ M4 可完全元数据驱动
- 待裁剪 5 个问题：natives 归属、并发默认值、fixture 范围、CLI 验收面、镜像降级策略

## M1 进展（2026-11-19 本轮交付：元数据解析层）

`dotnet test EsLuncher.slnx -c Release` → **33 passed / 0 failed**（0 warning）。

| 能力 | 落点 | 证据 |
|---|---|---|
| manifest 解析（925 版本 / 4 种 type） | `Metadata/VersionManifestParser` | 真实 manifest 通过 + fixture 单测 |
| version.json 解析（新/旧格式、javaVersion、assetIndex、logging、downloads） | `Metadata/VersionMetadataParser` | 真实 26.3 / 1.21.4 / 1.12.2 / 1.7.10 / 1.0 全部 OK |
| 规则求值（os.name / arch / version 正则 / features；缺省=允许，后者覆盖） | `Metadata/RuleEvaluator` | 8 个单测，含真实 legacy 形状 `[allow, disallow osx]` |
| 启动参数组装（规则过滤 + `${}` 展开 + 旧格式分词；先分词后展开） | `Launching/ArgumentAssembler` | 8 个单测，含含空格路径与缺值报错 |
| maven 坐标 → 下载路径 | `Metadata/MavenCoordinate` | 4 个单测 |

真实 JSON 交叉验证（临时工具，已清理）：26.3 libs=114/rules=61/natives=0、1.12.2 libs=39/natives=5、1.0 libs=13/natives=4。
**修正判断**：natives 不是"只有 1.7.10 有"，1.0 也有 4 个；1.13+ 改为 LWJGL3 原生 jar 直接进 classpath。

## M1 下载引擎（2026-11-19 本轮交付）

`dotnet test` → **60 passed / 0 failed**；真实下载 1.7.10 全量成功。

| 能力 | 落点 | 证据 |
|---|---|---|
| 单文件下载：主源 → 镜像回退、每源 3 次重试、Range 续传、大小+SHA1 校验、失败重下 | `Downloads/FileDownloader` | 9 个单测（含 5xx 重试、连接失败换镜像、忽略 Range 时重下、SHA1 不符重下） |
| 并发执行（默认 8、可配）、单文件失败不中断 | `Downloads/DownloadExecutor` | 2 个单测 |
| 计划生成：client / 库（含只有 natives 没有 artifact 的旧库）/ 资源索引 / 日志 / natives 解压 | `Downloads/DownloadPlanBuilder` | 7 个单测，覆盖 `${arch}`→32/64 |
| 镜像改写（BMCLAPI maven / assets / version client，全部实测 206） | `Downloads/Mirrors` | 计划断言 + 探活 |
| 清单与 version.json 缓存 + sha1 校验 | `Downloads/VersionCatalog` | 3 个单测（缓存命中、校验、未知版本） |
| natives 解压（跳过 `META-INF/`、防 zip slip） | `Downloads/NativesExtractor` | 2 个单测 |
| CLI 验收面 `esluncher fetch --version <id> [--dry-run] [--only ...] [--concurrency n]` | `src/EsLuncher.Cli` | 下列真实下载 |

**真实下载证据（1.7.10 全量）**：

| 项 | 结果 |
|---|---|
| 命令 | `esluncher.exe fetch --version 1.7.10` |
| 结果 | exit 0，**710 文件 / 133.1 MiB / 0 失败**，耗时 237 s（8 并发） |
| 资产 | 索引 686 条目 → 674 个唯一对象 / 107 MiB |
| 独立复核（不复用下载器代码） | 674 个对象逐个校验 size + SHA1：**0 bad / 0 missing**；client jar SHA1 与 version.json 一致 |
| natives | 解压 14 个文件到 `instances/1.7.10/natives`（jinput-dx8.dll 等） |
| 幂等重跑 | 第二次执行 **0.3 s，下载 0 / 跳过 710 / 失败 0** |
| 磁盘 | `D:\mc-data` 共 158.3 MiB（含清单与版本 JSON） |

**未演练**：真实网络中断后的续传（续传逻辑有单测，但没有真断网实验）→ 标 `TODO(需验证)`，M2 或夜跑时补。

## M2 启动核心（2026-11-19 本轮交付）

`dotnet test` → **79 passed / 0 failed**；真实 1.18.2 离线启动成功并正确退出。

| 能力 | 落点 | 证据 |
|---|---|---|
| Java 发现（只扫 JAVA_HOME + 常见目录，绝不读 PATH）+ 精确主版本匹配 | `Launching/JavaRuntime.cs` | `esluncher java` 找到 17/21；1.7.10 被拒绝并解释（exit 3） |
| classpath（元数据顺序、旧版 natives 不进、客户端最后） | `Launching/ClasspathBuilder.cs` | 3 个单测；1.18.2 实测 37 entries / missing 0 |
| 命令组装（双格式参数、logging、-Xmx、JVM 参数、工作目录） | `Launching/LaunchCommandBuilder.cs` | 5 个单测；`--dump` 可导出完整命令行 |
| 进程封装（日志落盘、到点杀进程树、退出码） | `Launching/ProcessRunner.cs` | 真实启动 38.1 s，窗口关闭后 exit 0 |
| 离线身份（MD5 v3 UUID） | `Launching/OfflineIdentity.cs` | 2 个单测；日志 `Setting user: Dev` |
| CLI `launch` / `java` | `src/EsLuncher.Cli` | 见 M2 文档验收表 |

**真实启动证据**：`esluncher launch --version 1.18.2 --username Dev` → 窗口 "Minecraft 1.18.2" 出现，
日志含 `Backend library: LWJGL version 3.2.2`、`OpenAL initialized`、`Sound engine started`；
手动关窗后游戏进程 exit 0，CLI 报 `autoStopped=False / exit=0`；`--autostop 30` 路径也验证通过。

**已知异常**：首次启动出现过一次 `0xC0000005`（无 hs_err），其后 3 次稳定 → 记为待复现风险。

## M3 认证（2026-11-19 本轮交付）

`dotnet test` → **91 passed / 0 failed**（新增 12 个认证测试）。

| 能力 | 落点 | 证据 |
|---|---|---|
| 离线身份 + 会话模型 | `Auth/AuthModels.cs`、`Launching/OfflineIdentity.cs` | 离线账号 `Dev` 的 UUID 与 OfflinePlayer 规则一致 |
| 微软设备码全链路（MSA→XBL→XSTS→MC→profile） | `Auth/MicrosoftAuthClient.cs` | 6 个测试覆盖成功链路 / pending / slow_down / 过期 / XSTS 错误码 / 401 |
| DPAPI 凭据落盘 | `Auth/Dpapi.cs`、`Auth/TokenStore.cs` | 文件头 `01 00 00 00 d0 8c 9d df`；文件内搜不到明文名字与 token |
| CLI `login/accounts/logout` + `launch --account` | `src/EsLuncher.Cli` | 实测保存/列出/删除；`launch --account dev` 正确注入身份 |

**未验证（要你提供）**：真实微软设备码登录需要 Azure 客户端 ID（公共客户端应用，个人账号免费）。
没有它时 CLI 只打印注册步骤并 exit 2，**不内置任何第三方 client ID**。

**按裁剪暂缓**：皮肤站（authlib-injector 外置登录）。

**M3 后增补（2026-11-19，20 分钟级）**：
- `esluncher versions [--type release|snapshot|old_alpha|old_beta|all] [--limit n]`：直接列清单（实测 925 个版本）。
- `scripts/smoke-m1m3.ps1`：一条命令跑完 build / test / accounts / launch dry-run / fetch 幂等 / versions / java，**7/7 通过**。
- 测试数 91 → **97**。

**重命名（2026-11-20）**：产品名改为 **ES Luncher**；目录 `D:\dev\es-luncher`、命名空间 `EsLuncher.*`、程序集 `EsLuncher` / `esluncher.exe`；数据根保持 `D:\mc-data`（复用已下载资源与运行时）。

## M4 Java 运行时（2026-11-20 本轮交付）

`dotnet test` → **108 passed / 0 failed**（新增 16 个 M4 测试）。

| 能力 | 落点 | 证据 |
|---|---|---|
| Mojang 运行时目录解析（8 个平台键） | `Java/JavaRuntimeCatalog.cs` | all.json 13,391 B；windows-x64 8 个组件 |
| 组件 manifest 解析 + 路径安全 | `Java/JavaRuntimeManifest.cs` | jre-legacy 208 条（192 file/16 dir）；`..`/绝对路径被拒（单测） |
| 安装（SHA1 逐文件、幂等、可执行位） | `Java/JavaRuntimeInstaller.cs` | jre-legacy **143.2 MiB/44.2 s**；java-runtime-beta **86.9 MiB/103.3 s** |
| 发现与选择（托管优先） | `Launching/JavaRuntime.cs` | `esluncher java` 列出 4 个（21/17 托管/17 系统/8 托管） |
| CLI | `esluncher java` / `java install` / `launch --install-java` | 见下 |

**真实启动证据**：

- `launch --version 1.7.10 --username Dev --autostop 40` → exit 0；`latest.log`：`Setting user: Dev`、
  **`LWJGL Version: 2.9.1`**、`Sound engine started`、blocks/items atlas 创建成功（托管 **Java 8**）。
- `launch --version 1.18.2 --autostop 30` → exit 0；选中**托管** `java-runtime-beta 17.0.15`（不是系统 JDK 17），全部 atlas 创建成功。
- 安装耗时：jre-legacy 143.2 MiB / 44.2 s；java-runtime-beta 86.9 MiB / 103.3 s（含逐文件 SHA1）。

**缺口**：不做运行时自动升级、不清理旧版本；Java 25/26.x 线未验证。

## M5 实例系统（2026-11-20 本轮交付）

`dotnet test` → **121 passed / 0 failed**（新增 13 个实例测试）。

| 能力 | 落点 | 证据 |
|---|---|---|
| 实例模型 + 原子 JSON 读写 | `Instances/InstanceStore.cs`（`InstanceConfig`） | 配置往返、`Save` 更新内存/窗口尺寸 |
| 创建/列表/克隆/删除/导出/导入 | 同上 + CLI `instance` 子命令 | 4 个实例实测；删除必须 `--force`；导入 zip-slip 被拒并清理 |
| 实例级启动 | `launch --instance <名字>` | `-Xmx1536m`、`working: instances\测试 实例\game`、natives 自动补齐 59 文件 |
| 并行多实例 | —— | **两个 "Minecraft 1.18.2" 窗口同时存在**，各自到主菜单，关窗后**两个都 exit 0**（各 64.4 s） |

**修掉的坑**：日志文件名原来只有「版本+秒级时间」，同秒并行会文件锁冲突；现为「实例名+版本+时间+4 位随机」，标签页 `launch-测试 实例-…-9f57.log` / `launch-dev-two-…-829b.log`。

**缺口**：导入其它启动器目录（.minecraft/Prism）未做；实例磁盘统计未做。

## M6 模组加载器（2026-11-20 本轮交付）

`dotnet test` → **131 passed / 0 failed**（新增 10 个合并/加载器测试）。

| 能力 | 落点 | 证据 |
|---|---|---|
| `inheritsFrom` 合并（库/参数/主类/资产继承、client jar 指向父版本） | `Loaders/VersionMerger.cs` | 5 个测试（顺序、覆盖、循环、缺父） |
| Fabric/Quilt：meta API 直出 profile JSON | `Loaders/LoaderProvisioner.cs` | Fabric 0.20.1、Quilt 0.20.0-beta.9 安装成功 |
| Forge/NeoForge：官方 installer + `--installClient` | 同上 | Forge 40.3.0 安装 92.7 s 成功 |
| CLI `loader list/install` + `instance create --loader` | `src/EsLuncher.Cli` | 见 M6 文档 |

**真实启动（1.18.2）**：Fabric classpath 45 → 主菜单 exit 0；Quilt 51 → exit 0；Forge 65（排除原版 jar）→ exit 0。

**修掉的坑**：① Fabric 库只有 `name+url` 导致 classpath 漏加载器库；② Forge installer 需要 `launcher_profiles.json`；③ Forge BootstrapLauncher 与原版 jar 的模块冲突。

**阻塞**：NeoForge —— `maven.neoforged.net` 本网络不可达（curl 000），且 1.20.1 资源 624.3 MiB 超过 500 MB 授权线。

## M7 界面（2026-11-20 本轮交付，进行中）

| 项 | 结果 |
|---|---|
| 图标 | 纯黑/纯白两套（1024/256 PNG + ICO），`ES` 偏上 + `luncher` 下方；`scripts/gen-icon.ps1` 可复现 |
| 应用 | `ApplicationIcon` + 窗口图标（`avares://EsLuncher/Assets/…`） |
| UI 壳 | 左侧导航（实例/下载/设置/关于），深色 Fluent，1120×720 |
| 实例页 | 真实读取 5 个实例（名称/版本/内存/账号/日期）；启动按钮接 `LaunchService`（继承链→Java→命令→natives→日志）；打开目录 |
| 关于页 | **Endless Studio** 图标+名称（仅此页出现），GPL/版本/里程碑摘要 |
| 下载页 | 925 条版本清单 + 正式/快照筛选 + 下载选中（进度条/取消，复用 fetch） |
| 下载页·加载器 | **无/Fabric/Quilt/Forge** 选择 + 「下载并建实例」一键完成（版本→加载器→依赖库→实例） |
| 设置页 | 数据根、默认内存、Java 路径、深/浅主题（`launcher-settings.json`，保存即生效） |
| 截图 | `docs/img/m7-instances.png`、`m7-downloads.png`、`m7-settings.png`、`m7-about.png` |
| 构建/测试 | 0 警告 0 错误；**134 tests 全绿** |

**未验证**：启动按钮未做点击级自动化（同一编排在 CLI 已验证）；下载页的 NeoForge 仍受网络/授权限制。

## M8 内容管理（2026-11-20 本轮交付，进行中）

`dotnet test` → **144 passed / 0 failed**（新增 10 个内容测试）。

| 能力 | 落点 | 证据 |
|---|---|---|
| mods / 资源包 / 光影包 列出与添加 | `Content/InstanceContent.cs` | `content add` 落 `instances\Fabric 测试\game\mods`（21 B），`list` 可见 |
| 移除 = 移入回收站（不真删） | 同上 | 文件在 `trash\content\Fabric 测试\20261120-192950-909-demo-mod.jar` |
| 路径安全 | 同上 | `../escape.jar` / `a/b.jar` 被拒（单测） |
| CLI | `esluncher content list/add/remove` | 实测通过 |
| UI 内容面板 | 实例页底部：类型切换 / 添加（文件选择器）/ 移除 / 打开目录 | 截图 `docs/img/m7-instances.png` |
| 模组元数据 | `Content/ModMetadata.cs` | Fabric `Example Mod · 1.2.3 · fabric`；Forge mods.toml；坏 jar 不崩 |

**测试**：**148 passed / 0 failed**（内容 10 + 元数据 4）

**扩展点（别做死）**：新增内容类型只加枚举+目录映射；`ContentFile` 可扩字段；将来依赖解析/Modrinth 接入读同一批文件，不改现有语义。

## 开发者支持（2026-11-20）

| 产物 | 说明 |
|---|---|
| `samples/MinimalLauncher` | 30 行内跑通「下载→选 Java→组装命令」，实测 1.18.2（跳过 3099、失败 0、classpath 37） |
| `docs/DEVELOPER-GUIDE.md` | 架构、接口速查、扩展点、7 条必须遵守的约定、GPL 说明 |
| `pack install --instance <新> <zip>` | 本地/下载的 .mrpack 一键建实例（依赖→原版→加载器→导入），实测 383 B 包 → 实例含 demo-mod.jar |

## 打磨批（2026-11-20）

`dotnet test` → **194 passed / 0 failed**，构建 0 警告。

- UI：实例搜索 + 分组筛选、日志窗口（实时 tail + 过滤 + 自动滚动）、诊断错误行搜索、关于页许可、设置页打开数据目录
- 资源包**顺序调整**（改 options.txt，自动 .bak）
- i18n 基础框架（导航/工具栏/搜索，中英；`ESLUNCHER_LANGUAGE` 可验收）
- 性能：模组更新检测并行 4 路
- 修复：`*.part` 不再显示；导入失败回滚实例；安装包导入句柄 bug；JsonNode 交换触发 "already has a parent"

## B/C 批：世界、更新、导入、体积（2026-11-20）

`dotnet test` → **189 passed / 0 failed**。

| 能力 | 证据 |
|---|---|
| 世界列表/体积/备份/还原/重命名 | CLI 实测：备份 zip、还原 `Demo World Restored`、重命名 `Demo World 2` |
| 截图管理 | `ContentKind.Screenshots`（UI 类型「截图」），删除走回收站 |
| 模组更新检测/更新 | `content outdated`：Fabric 测试 2 个均最新；`content update` 旧文件进回收站 |
| 依赖补装 UI | 诊断窗「补齐缺失前置」按钮（同名 slug → Modrinth 安装 → 复检） |
| 实例体积 | `instance size "FO 测试"` → 69.4 MiB |
| 导入其它启动器内容 | `instance import-dir` 复制 mods/saves/config/options.txt，源目录不动；单测覆盖 |

**未做**：实例分组/标签/搜索、数据根迁移、便携模式（下批打磨）。

## M8 内容管理器扩展（2026-11-20，完成）

`dotnet test` → **174 passed / 0 failed**。

| 能力 | 证据 |
|---|---|
| 内容类型 5 种：mods/资源包/光影/世界/数据包 | UI 类型下拉 + 世界下拉（数据包用）；截图 `docs/img/m8-content-datapacks.png` |
| 启用/禁用 | JEI `disable` → 列表 `[禁用]` → `enable` 恢复；`.disabled` 后缀 |
| 世界 | `content add --kind worlds <目录>` → `saves/Demo World`；UI 目录选择器 |
| 数据包 | `content add --kind datapacks --world "Demo World" <zip>` → 该世界 datapacks |
| 本地添加 | 文件选择器（文件类）/ 目录选择器（世界） |
| 回收站删除 | 统一进 `trash/content/<实例>/<时间戳>-<名字>` |
| Modrinth 按类型 | 光影检索/安装实测：`ComplementaryReimagined_r5.10.zip`（567 KB） |
| 安全 | 路径穿越/非法名拒绝；世界不可启用禁用 |

## M8 增补：Modrinth + .mrpack（2026-11-20）

`dotnet test` → **162 passed / 0 failed**。

| 能力 | 证据 |
|---|---|
| Modrinth 检索 | `modrinth search --query jei --version 1.18.2 --loader fabric` → 5 条（JEI 86,313,860 下载） |
| Modrinth 安装 | `modrinth install --instance "Fabric 测试" jei` → `jei-...jar`（1.1 MB） |
| 依赖实况 | 只装 JEI → 启动失败（`HARD_DEP fabric`）；装 `fabric-api` 后 → **`Loading 51 mods`**、JEI atlas 创建、退出 0 |
| .mrpack | 导出 971,964 B；导入同版本实例：下载 0 / 覆盖 1 / 跳过 0；`../` 路径被拒 |

## M9 诊断（2026-11-20，进行中）

| 能力 | 证据 |
|---|---|
| 错误行 + 崩溃报告 + 人话提示 | 缺依赖场景抓 3 条错误并提示「疑似缺少模组依赖」 |
| 诊断包 | `forge-diag.zip` 2,618 B，含 logs / crash-reports / config / system-info |
| CLI | `esluncher diagnose --instance <名字> [--export <zip>]` |
| **UI 诊断窗口** | 实例页「诊断」→ 错误行/崩溃/提示 + 导出；截图 `docs/img/m9-diagnostics.png` |
| 依赖体检 | `esluncher deps --instance`：读 modId/depends，内置与 fabric-api 子模块不误报；168 tests |

## M8 UI 增补：Modrinth 窗口（2026-11-20）

实例页「Modrinth」→ 按实例 MC/加载器检索并安装；截图 `docs/img/m8-modrinth.png`。

## M0–M9 验收复核（2026-11-21）

`scripts\verify-acceptance.ps1 -WithUi -WithLaunch` → **16 / 16 PASS（exit 0，44 s）**；`dotnet test` **202 passed / 0 failed**；构建 **0 警告 / 0 错误**。

| 里程碑 | 判据 | 复核结果 |
|---|---|---|
| M0 | 构建零警告 + 全量测试 | 0 警告 / 0 错误；202 passed（201 Core + 1 Headless UI） |
| M0/M7 | 空窗口能开能关 | `smoke-ui.ps1`：进程存活 8 s 后正常关闭（10.4 s） |
| M1 | 版本清单 + 下载计划 | `versions --type release` 有输出；`fetch 1.7.10 --dry-run` 出计划 |
| M2 | 1.18.2 命令组装 | `launch --version 1.18.2 --dry-run` 出 `-cp` |
| M3a | 离线账号可列 | `accounts` exit 0（不输出 token） |
| M4 | Java 发现 | `java` 列出托管与系统运行时 |
| M5 | 实例列表 / 体积 / 实例级启动 | `instance list`、`instance size`、`launch --instance` 全 exit 0 |
| M6 | 加载器真实启动（Fabric） | `launch --instance "Fabric 测试" --autostop 25` → **exit 0**；日志 `Loading 51 mods`、`Setting user: Player`、`Backend library: LWJGL version 3.2.2`、JEI atlas 创建成功 |
| M7 | UI 可启动 | 冒烟通过（见上） |
| M8 | 内容列举 | `content list --instance "Fabric 测试" --kind mods` exit 0 |
| M9 | 依赖体检 + 诊断 | `deps` / `diagnose` 均 exit 0 |

**同日修掉的数据问题**：`docs/ISSUES.md` GBK → UTF-8；`docs/RELEASE-CHECK.md` 原文件与 `artifacts/` 两份副本、git 两个历史版本均已损毁成 `锟斤拷` 乱码，**整篇重写**为 2026-11-21 实测版；13 条项目决策记忆从 `default-workspace` 迁到本项目根。

**本次未覆盖**（不算过也不算败）：NeoForge、微软真实登录、CurseForge、安装包重打包与签名、150% 缩放。

## M10 分发与自动更新（2026-11-21 本轮交付）

决策见 `docs/adr/0003-distribution-velopack.md`：**Velopack 1.2.161 全量接管**（安装包 / 便携包 / 自动更新 / 增量），Inno 退役。
`dotnet test` → **204 passed / 0 failed**；构建 0 警告。

| 能力 | 落点 | 证据 |
|---|---|---|
| 更新服务（检查/下载/应用） | `Core/Updates/UpdateService.cs`、`VelopackUpdateService.cs` | 接口不泄漏 Velopack 类型；参数顺序错误有 2 个单测 |
| 安装/更新钩子 | `App/Program.cs` | `VelopackApp.Build().Run()` 置于最前 |
| 无界面更新路径 | `App/HeadlessUpdate.cs` | `--update-test --source <dir> [--apply] [--log]`，输出 key=value |
| CLI | `esluncher update [--source] [--download] [--apply]` | 开发态明确拒绝（exit 2 + 人话），不假装成功 |
| 界面 | 「其他 → 更新」卡片 | 检查更新 / 下载并重启 / 状态行；中英词条 |
| 打包 | `scripts/pack-velopack.ps1` | Setup 21.4 MB、Portable 14.0 MB、full 14,049 KB |
| 增量 | 同目录连续打包 | **delta 15.3 KB / 37.1 KB**（对比 full 14,049 KB） |
| 安装 | `ESLuncher-win-Setup.exe --silent` | 装到 `%LocalAppData%\ESLuncher`，exit 0 |
| 升级 | `--update-test --apply` | 0.1.0 → 0.1.1 → … 重启后 `current` 正确递增 |
| **增量升级（关键判据）** | 源目录**只留 delta**、删掉 full 包 | 0.1.2 → **0.1.3 成功**；源里没有 14 MB 的 full，证明走的是增量 |
| 卸载 | `Update.exe uninstall --silent` | 安装目录整体移除 |

**未做**：GitHub Releases 更新源（暂无 git remote，先本地目录源）、启动时自动检查。

**签名（同日补）**：`scripts\pack-velopack.ps1` 支持 `-PfxPath` / `-SignParams` / `-AzureTrustedSignFile`，逐文件签由 `scripts\sign-file.ps1`（`Set-AuthenticodeSignature`，无需 signtool）完成。
自签实测：0.1.4 的 Setup.exe / Portable 内 `current\EsLuncher.exe` / nupkg 内 `lib/app/EsLuncher.exe` 全部带上 Authenticode 签名 + DigiCert 时间戳；
`Status=UnknownError`（自签根不受信，签名本身有效）。**真正解决 SmartScreen 仍需你提供真实证书**（pfx 或 signtool/Azure 凭据）。

## M11 性能与稳定（2026-11-21 本轮交付，进行中）

基准脚本：`scripts\bench-startup.ps1` / `bench-memory.ps1` / `bench-download.ps1`，数字与方法全在 `docs/M11-performance.md`。

| 指标 | 优化前 | 优化后 | 变化 |
|---|---|---|---|
| 冷启动中位（5 次） | 1306 ms | **948 ms** | −27% |
| 常驻工作集 | 124.6 MB | **112.1 MB** | −12.5 MB |
| XAML 加载 | ~600 ms | **~166 ms** | −72% |
| 下载吞吐（1.7.10 assets，官方源） | —— | **1.62 MB/s**（676 文件 / 107.4 MB / 66.4 s，0 失败） | —— |
| 镜像对比（BMCLAPI） | —— | 0.20 MB/s + 5 个 TLS 失败 → **默认仍走官方** | —— |

**怎么做到的**（先埋点再改，不猜）：`StartupTrace`（`ESLUNCHER_STARTUP_TRACE=1` → `%TEMP%\esluncher-startup.log`）显示瓶颈在 XAML 加载。
于是把下载/设置/关于三页改成按需创建（`ContentControl` 宿主 + `AboutView` 抽离），账号名改后台 DPAPI 异步填充，图片按目标尺寸解码。

**挂机**：20 分钟采样里 2 分钟涨约 30 MB 后即进入平台期（漂移 < 1 MB），未见泄漏；但进程在 17 分钟时自行退出，事件日志无崩溃记录 → 记 `TODO(需复现)`。**用户要求暂停常驻内存测试**，2 小时级留到下次。

**界面同批**：关于页重做（运行环境 / 第三方组件 / 数据与服务 / 鸣谢四卡；组件版本运行时现读）；鸣谢标题去掉「开发者」字样、条目改为贡献描述；实例页布局纠正（搜索行不再用 margin 硬凑、卡片自适应、空态、汇总行、工具栏补无障碍名）。详见 `docs/M7-ui.md` §6。

## 下一步（M11 收尾 → M12）

1. 复现并定位「挂机 17 分钟自行退出」；加 `UnhandledException` 落盘与崩溃包。
2. 2 小时挂机 + 边下边玩场景的内存曲线；下载吞吐多次采样取中位数。
3. 大版本（26.x，587 MiB / 5,149 对象）下载需要你单独批准授权线。
4. 之后 M12：发行说明、第三方许可清单落文件、回滚方案。

## 待用户提供（做界面/关于页时提醒）

**开发者名单**需要你提供：**工作室名称** + **工作室图标（png，最好是正方形）**。
UI 的「关于/开发者」页（M7）与 README/发行说明（M12）会用到；做到时我会先问你再动手。
