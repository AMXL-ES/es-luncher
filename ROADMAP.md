# ROADMAP · ES Luncher

> 每个里程碑 = 一轮会话的主轴。**完成判据必须是可执行证据**（命令 + 输出 + 产物路径），不是形容词。
> 顺序是硬约束：M1/M2 用 CLI 验收，M7 之前不写任何 UI 业务逻辑。

| 里程碑 | 内容 | 完成判据 | 状态 |
|---|---|---|---|
| **M0** | 环境 + 骨架 | 工具链版本齐全；探活表成型；`dotnet build -c Release` 零警告；空窗口能开能关且记录冷启动毫秒数 | 🟡 证据齐备，待验收 |
| **M1** | 版本元数据 + 下载引擎 | manifest→version.json→libraries→assets→natives 全量下载；SHA1 全校验；断点续传；并发可配；真实 version.json fixture 单测 | 🟡 完成待验收（60 tests；1.7.10 全量 710 文件/133 MiB 实测，幂等重跑 0.3 s） |
| **M2** | 启动核心 | javaw 命令组件化拼装（新旧双格式 arguments、规则求值、natives 解压、classpath 顺序、logging、JVM 参数、内存、工作目录）；起真实客户端并正确退出 | 🟡 完成待验收（1.18.2 离线起客户端 + 窗口关闭后 exit 0；Java 8 版本待 M4 解锁） |
| **M3a** | 离线认证 | 离线 UUID（OfflinePlayer 规则）、profile 落盘、`--username/--uuid` 正确注入 | ✅ 完成（DPAPI 存储 + CLI，M2 已真实使用） |
| **M3b** | 微软认证（延后） | 设备码全链路 MSA→XBL→XSTS→minecraft login→profile→entitlements；自动续期；401 自动重登。**阻塞项：Azure 客户端 ID（用户提供）** | 🟡 代码完成（12 测试），真实登录待 client ID；续期 API 已实现未接启动流程 |
| **M3c** | 皮肤站外置登录（延后） | authlib-injector 注入 + 外置登录 API 适配（多站兼容） | ⏸ 按裁剪暂缓（`IAuthProvider` 已留扩展点） |
| **M4** | Java 运行时管理 | 探测本机 JDK（**须跳过 PATH 上崩掉的 java**）；按 `javaVersion.majorVersion` 选；按需下载 Mojang 官方运行时；多版本共存；选错给人话原因 | ✅ 完成（jre-legacy 143 MiB / java-runtime-beta 86.9 MiB 实测；1.7.10 与 1.18.2 均在托管运行时上真启动） |
| **M5** | 实例系统 | 版本隔离；每实例独立配置；多实例并存可跑；克隆/导入/导出 | ✅ 完成（4 个实例；**两个 1.18.2 实例同时运行并各自 exit 0**；导入含 zip-slip 校验） |
| **M6** | 模组加载器 | Fabric/Quilt/NeoForge/Forge 安装；`inheritsFrom` 继承链正确解析 | 🟡 Fabric/Quilt/Forge 已真启动（1.18.2）；NeoForge 阻塞：maven 主机不可达 + 1.20.1 资源 624 MB 需授权 |
| **M7** | UI/UX | 实例列表、下载页、设置页、进度与错误呈现、键盘可达、深浅主题、i18n、高 DPI（专业玩家密度） | 🟡 进行中：图标 + UI 壳 + 实例页（真实数据/启动接线）+ 关于页（Endless Studio）；下载/设置占位 |
| **M8** | 模组与整合包 | Modrinth/CurseForge 检索安装、依赖解析、冲突提示、`.mrpack` 与 CF zip 导入导出（**CurseForge 需 API key**） | 待办 |
| **M9** | 诊断能力 | 崩溃报告解析、日志高亮搜索、缺失依赖/Java 不匹配检测、一键导出诊断包 | 待办 |
| **M10** | 分发 | NSIS/MSI 安装包、自动更新（Velopack 候选）、增量更新、便携模式、代码签名方案 | 待办 |
| **M11** | 性能与稳定 | 冷启动耗时、内存上限、下载吞吐、长时挂机基准报告（全有数字） | 待办 |
| **M12** | 1.0 发布 | 全量回归、文档、发布说明、回滚方案、安全与许可清单 | 待办 |

## 里程碑内部纪律

1. 一次只推进一个里程碑；做完 → 跑判据命令 → 更新 `PROGRESS.md` → 语义化 commit → 汇报 → 停手等验收。
2. 每个里程碑开工前问一次范围裁剪（做多少、砍什么、验收标准认不认）。
3. 判据不通过不许进下一个里程碑。
