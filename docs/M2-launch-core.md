# M2 · 启动核心（设计 + 验收）

> 目标：把"下载好的版本"变成"真的能跑起来的客户端"。本里程碑只做**离线**启动（微软正版在 M3b、皮肤站在 M3c）。

## 1. 组成

| 组件 | 落点 | 职责 |
|---|---|---|
| Java 发现 | `Launching/JavaRuntime.cs`（`JavaDiscovery`） | 只扫 `JAVA_HOME` 与常见安装目录，逐个实测 `java -version`；**绝不读 PATH** |
| Java 选择 | `Launching/JavaRuntime.cs`（`JavaSelector`） | 按 `version.json.javaVersion.majorVersion` **精确匹配**；匹配不到给人话解释 |
| 进程封装 | `Launching/ProcessRunner.cs` | 抓输出（探测）或重定向到日志文件（启动游戏）；支持到点杀进程树 |
| classpath | `Launching/ClasspathBuilder.cs` | 按元数据顺序，只放"规则允许且带 artifact"的库，客户端 jar 最后 |
| 离线身份 | `Launching/OfflineIdentity.cs` | `MD5("OfflinePlayer:"+name)` 置 v3，仅本地离线用 |
| 命令组装 | `Launching/LaunchCommandBuilder.cs` | JVM 参数 + 日志配置 + `-cp` + 主类 + game 参数 |
| 验收面 | `src/EsLuncher.Cli`（`launch` / `java`） | 打印命令行、检查 classpath、真实启动 |

## 2. 关键约定（不可随意改）

1. **绝不按 PATH 找 java**：本机 PATH 上的 `java` 是坏的（Oracle javapath，退出码 `0xC0000409`）。
2. **Java 主版本精确匹配**：1.7.10/1.12.2 → 8；1.17.x → 16；1.18.x–1.20.4 → 17；1.20.5+/26.x → 21/25。选不到就**拒绝启动并解释**，不猜。
3. **natives 与游戏目录分离**：`instances/{id}/natives` 与 `instances/{id}/game`，不污染 `libraries`。
4. **classpath 顺序 = 元数据顺序**；旧版 natives jar **不进 classpath**（解压用），新版 LWJGL3 原生 jar 作为普通库**进 classpath**。
5. 进程 stdout/stderr **全部落日志文件**（`D:\mc-data\logs\launch-*.log`），不靠内存缓冲。
6. 验收用 `--autostop <秒>`：撑到时间点即算"起来了"，然后杀进程树，避免无人值守的客户端一直挂着。

## 3. 命令行顺序

```
java.exe
  -Xmx<memory>m [额外 JVM 参数]
  <元数据 jvm 参数（含 -Djava.library.path）>   ← 旧格式没有，则补 -Djava.library.path
  -Dlog4j.configurationFile=<assets/log_configs/...>  ← 有 logging 段时
  -cp <classpath>
  <mainClass>
  <game 参数（双格式，由 ArgumentAssembler 生成）>
```

## 4. 已知缺口

- **M4**：按需下载 Mojang 官方 Java 运行时（`javaVersion.component` 已解析，下载器未实现）——这是"1.7.10 无法在本机启动"的唯一原因。
- **M3b/M3c**：正版 token、皮肤站外置登录。
- **M6**：`inheritsFrom` 叠加后 classpath/主类会变，届时 `ClasspathBuilder` 要能处理继承链。
- 世界/存档目录、窗口参数、JVM GC 调优属于后续里程碑。

## 5. 验收记录（2026-11-19）

| 项 | 结果 |
|---|---|
| Java 发现 | `esluncher java` → **Java 21** `D:\dev\jdk-21`、**Java 17** `C:\Program Files\Microsoft\jdk-17.0.12.7-hotspot`；PATH 上的坏 java 被忽略 |
| 选不中时拒绝 | `launch --version 1.7.10 --dry-run` → **exit 3**，"这个版本需要 Java 8，但本机没有。本机可用: Java 21, Java 17" |
| 精确匹配 | `launch --version 1.18.2` → 自动选 **Java 17** |
| 命令自检 | classpath **37 entries / missing 0**；natives 38 文件；参数 35 条 |
| 真实启动（离线） | 窗口标题 **"Minecraft 1.18.2"** 出现；日志含 `Setting user: Dev`、`Backend library: LWJGL version 3.2.2 build 10`、`OpenAL initialized`、`Sound engine started`、全部 texture atlas 创建成功 |
| 正确退出 | `CloseMainWindow` → 游戏进程退出码 **0**，CLI 报告 `autoStopped=False / exit=0 / 38.1 s`；`latest.log` 以 `Stopping!` 结束 |
| 自动停止路径 | `--autostop 30` → `autoStopped=True`（杀进程树），CLI exit 0 |
| 观测异常 | **首次启动出现过一次 `0xC0000005`**（6.8 s，无 hs_err 文件），其后 3 次启动均稳定；怀疑首次 GL/音频驱动初始化，待复现 |
| 数据量 | `D:\mc-data` 共 **587.5 MiB**（1.7.10 + 1.18.2；两者共享 494 个资源对象，1.18.2 实际只新下 423 MiB） |

**离线模式的预期报错**：日志里有 `Failed to verify authentication`（ERROR）——`accessToken=0` 的离线启动必然出现，与 Prism/HMCL 离线模式一致，不是缺陷。

**未验证**：1.7.10 / 1.12.2 等 Java 8 版本（本机无 Java 8）→ 由 M4 的运行时下载解锁后再验。
