# PROGRESS · GLauncher

> 唯一事实源：本文件回答「现在在哪」。历史细节进 `docs/`，决策进 `docs/adr/`，默认值进 `docs/DECISIONS.md`。
> **每轮开局三读**：本文件 → `docs/INDEX.md` → 当前里程碑文档。

- **目标 id**：`goal-db5cdd51-065f-4a37-a25d-883614f0ab16`
- **当前阶段**：**M4 完成，待用户验收**（2026-11-20）：Java 运行时下载与多版本共存跑通，1.7.10/1.18.2 均在托管运行时上真实启动
- **项目根**：`D:\dev\mc-launcher` ｜ **数据根**：`D:\mc-data` ｜ **不占 C 盘**
- **上游契约**：`C:\Users\HP\Desktop\Minecraft启动器-项目启动提示词.md`

## 已定（不可逆，已确认）

| 决策 | 值 |
|---|---|
| 技术栈 | .NET 10（SDK 10.0.401）+ Avalonia 12.1.6；引擎 `GLauncher.Core` 零 UI 依赖 |
| 许可证 | **GPL-3.0-or-later**（现在公开）→ 可合法移植 Prism/MultiMC，须保留声明 |
| 平台 | Windows 优先，架构跨平台 |
| 认证 | **先只做离线**；为微软正版（M3b，阻塞于 Azure client ID）与皮肤站（M3c，authlib-injector）预留 Provider 接口 |
| 用户定位 | 专业玩家/开发者，信息密度优先，不做"简单模式" |
| 交付顺序 | 自底向上：先引擎（CLI 验收），M7 之前不写 UI 业务逻辑 |
| 下载源 | 官方优先 → 失败降级镜像 → 手动可切换 |
| 目录 | 工程 `D:\dev\mc-launcher`，数据 `D:\mc-data` |

## M0 验收证据（2026-11-19）

| 判据 | 命令 | 结果 |
|---|---|---|
| 构建零警告 | `dotnet build GLauncher.slnx -c Release` | **exit 0，0 warning / 0 error**；产物 `src/GLauncher.App/bin/Release/net10.0/GLauncher.exe`（0.2 MB，framework-dependent） |
| 空窗口能开能关 | `powershell -NoProfile -File scripts\smoke-m0.ps1` | **exit 0**；冷启动 **933 ms / 696 ms**（两次）；工作集 **102.3 / 99.0 MB**；`CloseMainWindow` 正常退出，exit=0 |
| 探活表成型 | `powershell -NoProfile -File scripts\probe-network.ps1 -JsonOut D:\mc-data\logs\net.json` | **8/8 符合预期**；官方 manifest 1.43 s、BMCLAPI 0.18 s、CurseForge 403（需 key） |
| 测试骨架可用 | `dotnet test GLauncher.slnx -c Release` | exit 0；**2 passed / 0 failed**（含架构守卫 `Core_must_not_reference_any_ui_framework`） |
| 事实源 | —— | `PROGRESS.md` / `ROADMAP.md` / `AGENTS.md` / `docs/env-report.md` + ADR 0001/0002 |
| 版本库 | `git log --oneline` | 首提交 `chore: scaffold GLauncher M0` |

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

`dotnet test GLauncher.slnx -c Release` → **33 passed / 0 failed**（0 warning）。

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
| CLI 验收面 `glauncher fetch --version <id> [--dry-run] [--only ...] [--concurrency n]` | `src/GLauncher.Cli` | 下列真实下载 |

**真实下载证据（1.7.10 全量）**：

| 项 | 结果 |
|---|---|
| 命令 | `glauncher.exe fetch --version 1.7.10` |
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
| Java 发现（只扫 JAVA_HOME + 常见目录，绝不读 PATH）+ 精确主版本匹配 | `Launching/JavaRuntime.cs` | `glauncher java` 找到 17/21；1.7.10 被拒绝并解释（exit 3） |
| classpath（元数据顺序、旧版 natives 不进、客户端最后） | `Launching/ClasspathBuilder.cs` | 3 个单测；1.18.2 实测 37 entries / missing 0 |
| 命令组装（双格式参数、logging、-Xmx、JVM 参数、工作目录） | `Launching/LaunchCommandBuilder.cs` | 5 个单测；`--dump` 可导出完整命令行 |
| 进程封装（日志落盘、到点杀进程树、退出码） | `Launching/ProcessRunner.cs` | 真实启动 38.1 s，窗口关闭后 exit 0 |
| 离线身份（MD5 v3 UUID） | `Launching/OfflineIdentity.cs` | 2 个单测；日志 `Setting user: Dev` |
| CLI `launch` / `java` | `src/GLauncher.Cli` | 见 M2 文档验收表 |

**真实启动证据**：`glauncher launch --version 1.18.2 --username Dev` → 窗口 "Minecraft 1.18.2" 出现，
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
| CLI `login/accounts/logout` + `launch --account` | `src/GLauncher.Cli` | 实测保存/列出/删除；`launch --account dev` 正确注入身份 |

**未验证（要你提供）**：真实微软设备码登录需要 Azure 客户端 ID（公共客户端应用，个人账号免费）。
没有它时 CLI 只打印注册步骤并 exit 2，**不内置任何第三方 client ID**。

**按裁剪暂缓**：皮肤站（authlib-injector 外置登录）。

**M3 后增补（2026-11-19，20 分钟级）**：
- `glauncher versions [--type release|snapshot|old_alpha|old_beta|all] [--limit n]`：直接列清单（实测 925 个版本）。
- `scripts/smoke-m1m3.ps1`：一条命令跑完 build / test / accounts / launch dry-run / fetch 幂等 / versions，**6/6 通过**。
- 测试数 91 → **97**。

## M4 Java 运行时（2026-11-20 本轮交付）

`dotnet test` → **108 passed / 0 failed**（新增 16 个 M4 测试）。

| 能力 | 落点 | 证据 |
|---|---|---|
| Mojang 运行时目录解析（8 个平台键） | `Java/JavaRuntimeCatalog.cs` | all.json 13,391 B；windows-x64 8 个组件 |
| 组件 manifest 解析 + 路径安全 | `Java/JavaRuntimeManifest.cs` | jre-legacy 208 条（192 file/16 dir）；`..`/绝对路径被拒（单测） |
| 安装（SHA1 逐文件、幂等、可执行位） | `Java/JavaRuntimeInstaller.cs` | jre-legacy **143.2 MiB/44.2 s**；java-runtime-beta **86.9 MiB/103.3 s** |
| 发现与选择（托管优先） | `Launching/JavaRuntime.cs` | `glauncher java` 列出 4 个（21/17 托管/17 系统/8 托管） |
| CLI | `glauncher java` / `java install` / `launch --install-java` | 见下 |

**真实启动证据**：

- `launch --version 1.7.10 --username Dev --autostop 40` → exit 0；`latest.log`：`Setting user: Dev`、
  **`LWJGL Version: 2.9.1`**、`Sound engine started`、blocks/items atlas 创建成功（托管 **Java 8**）。
- `launch --version 1.18.2 --autostop 30` → exit 0；选中**托管** `java-runtime-beta 17.0.15`（不是系统 JDK 17），全部 atlas 创建成功。
- 安装耗时：jre-legacy 143.2 MiB / 44.2 s；java-runtime-beta 86.9 MiB / 103.3 s（含逐文件 SHA1）。

**缺口**：不做运行时自动升级、不清理旧版本；Java 25/26.x 线未验证。

## 下一步（M5 实例系统）

按 roadmap 顺序进入 M5：版本隔离、每实例独立配置（内存/Java/参数/窗口）、多实例并存、克隆/导入/导出。
开工前会先问 3–5 个裁剪问题（实例目录布局、是否复用 `assets/libraries`、导入已有 `.minecraft` 的边界）。
