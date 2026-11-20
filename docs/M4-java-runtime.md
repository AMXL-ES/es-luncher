# M4 · Java 运行时管理（探测 / 下载 / 多版本共存 / 选择）

> 目标：**不装系统 JDK、不改 PATH**，由启动器按版本要求自己拿到合适的 Java。
> 结果：Mojang 官方运行时下载 + 多版本共存完成；1.7.10 与 1.18.2 都在**托管运行时**上真实启动成功。

## 1. 数据源（实测 2026-11-19）

| 数据 | 实测 |
|---|---|
| 运行时目录 `all.json` | `https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json`，13,391 B |
| 平台键 | `gamecore` / `linux` / `linux-i386` / `mac-os` / `mac-os-arm64` / `windows-arm64` / `windows-x64` / `windows-x86` |
| windows-x64 组件 | `jre-legacy`、`java-runtime-alpha/beta/gamma/gamma-snapshot/delta/epsilon`、`minecraft-java-exe` |
| 组件 manifest | 每个组件给 `manifest.url + sha1`，再列出文件表 |
| jre-legacy 文件表 | **208 条目 = 192 file + 16 directory**，raw 合计 **143.2 MiB** |
| 文件条目 | `type`(file/directory/link)、`downloads.raw{url,sha1,size}`、`executable` |

## 2. 实现

| 能力 | 落点 |
|---|---|
| 平台判定 + 目录解析 | `Java/JavaRuntimeCatalog.cs`（`JavaPlatform.Detect`） |
| 组件 manifest 解析（含路径安全校验） | `Java/JavaRuntimeManifest.cs` |
| 下载安装（逐文件 SHA1、断点复用、防路径穿越、可执行位） | `Java/JavaRuntimeInstaller.cs` |
| 发现与选择 | `Launching/JavaRuntime.cs`（`JavaDiscovery` / `JavaSelector`） |
| CLI | `glauncher java` / `glauncher java install` / `launch --install-java` |

约定：

1. **托管运行时优先**：`Discover(extraRoots)` 把显式根（`D:\mc-data\java`）排在系统安装之前，同主版本先选托管版。
2. 安装目录 `D:\mc-data\java\<组件>\<版本>\`，与系统 JDK 完全隔离；删目录即可回滚。
3. 已存在且 SHA1 匹配的文件直接跳过；重复安装不重复下载（单测覆盖）。
4. `link` 类型条目不跟随，避免越界。
5. 只下载 `downloads.raw`（Mojang 同时给 lzma，我们不用压缩包，省一层解压与校验）。
6. `launch --install-java`：缺 Java 时自动装该版本元数据要求的组件，再重新选择。

## 3. 验收记录（2026-11-19/20）

| 项 | 结果 |
|---|---|
| 测试 | `dotnet test` → **108 passed / 0 failed**（新增 16 个 M4 测试：目录解析、manifest 解析、路径穿越拒绝、安装、幂等） |
| 安装 jre-legacy | `java install --version 1.7.10` → **143.2 MiB / 192 文件 / 44.2 s**，探测到 **Java 8** |
| 安装 java-runtime-beta | `java install --version 1.18.2`（该版本元数据要 `java-runtime-beta`）→ **86.9 MiB / 403 文件 / 103.3 s**，探测到 **Java 17** |
| 多版本共存 | `glauncher java` → Java 21（系统）、Java 17（托管）、Java 17（系统）、Java 8（托管），共 4 个 |
| 旧版本真实启动 | `launch --version 1.7.10 --username Dev --autostop 40` → exit 0；`latest.log`：`LWJGL Version: 2.9.1`、`Sound engine started`、贴图 atlas 创建成功 |
| 现代版本真实启动 | `launch --version 1.18.2 --autostop 30`（**托管 Java 17**）→ exit 0，全部 atlas 创建成功 |
| 选择优先级 | 1.18.2 选中 `D:\mc-data\java\java-runtime-beta\17.0.15\bin\java.exe`（托管），而非系统 JDK 17 |

## 4. 已知缺口

1. **不做运行时自动升级**：组件版本跟随 Mojang 目录；要更新就 `java install --refresh`（会重新解析目录后按缺失文件补齐）。
2. **不清理旧版本**：`java/<组件>/<版本>` 多版本并存，占磁盘；清理策略留到 M10/M11。
3. 尚未验证 Java 25（`java-runtime-epsilon`，26.x 线用）与 26.x 启动。
4. 非 Windows 只做了路径与可执行位的代码路径，未真机验证。
