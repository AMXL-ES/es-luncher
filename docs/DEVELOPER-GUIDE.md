# 用 EsLuncher.Core 写自己的启动器

> 面向想基于本项目做自己启动器的开发者。代码是 **GPL-3.0**：可以读、改、fork；**分发**衍生作品时必须同样 GPL-3.0 并公开源码。品牌（ES Luncher / Endless Studio 名称与图标）不随代码授权。
> 完整示例见 `samples/MinimalLauncher`。

## 1. 架构一句话

```
UI (Avalonia, src/EsLuncher.App)   CLI (src/EsLuncher.Cli)
                └──────────────┬──────────────┘
                        EsLuncher.Core（零 UI 依赖）
  Metadata → Downloads → Java → Launching → Loaders → Auth → Instances → Content → Diagnostics
```

- 所有业务在 Core；UI/CLI 只做编排与展示。
- Core 不读环境变量、不弹窗、不打印；路径通过 `DataLayout` 注入，HTTP 走 `IRemoteFileClient`。

## 2. 最小启动器（示例可直接跑）

```powershell
dotnet run --project samples/MinimalLauncher -- D:\mc-data 1.18.2
```

输出（实测）：

```
数据根 D:\mc-data / 版本 1.18.2
下载 0，跳过 3099，失败 0，资源对象 3052
匹配 Java 17: D:\mc-data\java\java-runtime-beta\17.0.15\bin\java.exe
classpath : 37 项
```

代码骨架（完整版见 `samples/MinimalLauncher/Program.cs`）：

```csharp
var layout = new DataLayout(root);
using var client = new HttpRemoteFileClient();
var downloader = new FileDownloader(client);
var catalog = new VersionCatalog(layout, downloader);
var fetcher = new VersionFetcher(layout, downloader);

await fetcher.FetchAsync("1.18.2", new FetchOptions(), null, ct);

var manifest = await catalog.GetManifestAsync(false, ct);
var version = await catalog.GetVersionAsync("1.18.2", manifest, false, ct);
var java = JavaSelector.Select(version.JavaVersion!.MajorVersion!.Value,
    JavaDiscovery.Discover([layout.JavaDir]), null).Runtime!;
var command = LaunchCommandBuilder.Build(version, LaunchEnvironment.Detect(), layout, java,
    new LaunchRequest { Username = "Dev" });
await ProcessRunner.RunLoggedAsync(command, logPath, null, ct);
```

## 3. 核心接口速查

| 能力 | 入口 |
|---|---|
| 数据目录布局 | `Downloads.DataLayout`（根目录由你给） |
| HTTP | `IRemoteFileClient` / `HttpRemoteFileClient`（可换成代理实现） |
| 下载 | `FileDownloader`、`DownloadExecutor`、`DownloadPlan/Job`（SHA1、断点、并发） |
| 版本清单 | `VersionCatalog`（manifest 缓存 + `version.json` 本地回退） |
| 版本继承 | `Loaders.VersionMerger`（inheritsFrom 合并，classpath 顺序） |
| 下载编排 | `VersionFetcher`（client/libraries/assets/natives 计划） |
| 启动 | `LaunchCommandBuilder`（规则求值）、`ProcessRunner.RunLoggedAsync` |
| Java | `JavaDiscovery`、`JavaSelector`、`JavaRuntimeInstaller`、`JavaRuntimeCatalog` |
| 加载器 | `LoaderProvisioner`（Fabric/Quilt meta；Forge/NeoForge installer） |
| 认证 | `OfflineAuthProvider`、`MicrosoftAuthClient`、`DpapiTokenStore` |
| 实例 | `InstanceStore`、`InstanceConfig` |
| 内容 | `InstanceContentService`（5 类）、`ModrinthClient`、`InstancePackService`、`ModpackInstaller` |
| 诊断 | `InstanceDiagnostics`（日志/崩溃/提示/诊断包） |

## 4. 扩展点

- **新加载器**：实现"拿到 version JSON"的安装器（meta API 或 installer），交给 `VersionMetadataParser`；启动侧靠 `inheritsFrom` 合并自动生效。
- **新内容类型**：`ContentKind` 加枚举 + `FolderName` 映射；UI/CLI 的 `TryParseKind` 同步一条 case。
- **新下载源/镜像**：实现 `IRemoteFileClient` 或给 `DownloadJob` 加 fallback URL。
- **新认证**：实现 `IAuthProvider`，返回 `AuthSession`。
- **换 UI**：Core 不依赖 Avalonia；WPF/WinUI/Web 均可直接引用。

## 5. 必须遵守的约定（踩过的坑）

1. `user_type` 由认证方式决定（离线 `legacy`，微软 `msa`）；不要猜。
2. Forge/NeoForge 1.17+ 用 `BootstrapLauncher`，**原版 jar 不能进 `-cp`**（否则模块重复导出直接崩）。
3. Fabric/Quilt 的库常只有 `name + url`（没有 `downloads.artifact`），classpath 要按 maven 坐标回退。
4. natives 按实例解压（并行多实例会互相覆盖）。
5. 日志文件名带实例名+随机，避免同秒启动撞名。
6. 删除用户内容一律进回收站，禁止 `File.Delete` 直接删。
7. 加载器 installer 需要目标目录有 `launcher_profiles.json`（写最小 stub）。

## 6. 本仓库不提供的

- Mojang 资源（client.jar / assets）—— 运行时下载，不随源码分发。
- 微软 OAuth 的 client ID —— 需自行在 Azure 注册公共客户端（不内置任何第三方 ID）。
- 品牌授权（图标/名称）。

## 7. 想贡献/扩展时

- 先跑 `scripts/smoke-m1m3.ps1`（8/8）与 `dotnet test`（174 tests）。
- 新逻辑写进 Core 并配单测；UI 只做编排。
- 文档按 `docs/M*.md` 归档，决策写 `docs/DECISIONS.md`。
