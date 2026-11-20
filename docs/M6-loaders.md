# M6 · 模组加载器（Fabric / Quilt / Forge / NeoForge）

> 用户裁剪：四个全做；Fabric 用官方 meta API 直出 JSON；加载器版本可列可选（默认最新稳定）；OptiFine/LiteLoader 暂缓；验收 Fabric 1.18.2 + NeoForge 1.20.1。
> 结果：**Fabric / Quilt / Forge 三个都在 1.18.2 上真实启动进主菜单**；NeoForge 被网络阻断（见 §5）。

## 1. `inheritsFrom` 合并（`Loaders/VersionMerger.cs`）

加载器版本 JSON 只写增量，其余继承原版。合并规则：

| 字段 | 规则 |
|---|---|
| `mainClass` | 子版本覆盖（Fabric = KnotClient；Forge = BootstrapLauncher） |
| `libraries` | **父在前、子在后**追加（classpath 顺序 = 元数据顺序） |
| `arguments.game/jvm` | 父在前、子在后拼接 |
| `assets` / `assetIndex` / `javaVersion` / `logging` / `downloads.client` | 子缺失则继承父 |
| `client.jar` 位置 | 新增 `ClientJarVersionId`：加载器版本没有自己的 client.jar，指向原版父版本 |
| 循环/过深 | 深度 > 10 或父缺失直接报错 |

## 2. 加载器安装（`Loaders/LoaderProvisioner.cs`）

| 加载器 | 方式 |
|---|---|
| Fabric / Quilt | 官方 meta API：`/versions/loader/{mc}` 列表 + `/{mc}/{loader}/profile/json` → 原样写入 `versions/<id>/<id>.json` |
| Forge / NeoForge | 官方 installer jar + `java -jar <installer> --installClient <dataRoot>`；installer 自己写 libraries 与 versions |

- Forge installer 要求目标目录有 `launcher_profiles.json`，否则报 "you need to run the launcher first" → 首次自动写最小 stub。
- 默认版本：Fabric/Quilt 取列表里第一个 stable（Quilt 元数据没有 stable 标记时退回第一个）；Forge 取 `promotions_slim.json` 的 recommended，再退 latest。
- 安装后仍需 `fetch --version <loader 版本 id>` 下载加载器依赖库（与普通版本同一套下载引擎）。

## 3. CLI

```powershell
esluncher loader list    --version 1.18.2 --loader fabric
esluncher loader install --version 1.18.2 --loader fabric
esluncher instance create "Fabric 测试" --version 1.18.2 --loader fabric
esluncher launch --instance "Fabric 测试" --autostop 35
```

## 4. 验收记录（2026-11-20）

| 加载器 | 版本 | classpath | 结果 |
|---|---|---|---|
| Fabric | 0.20.1 / 1.18.2 | 45（原版 37 + Fabric 8） | 进主菜单，35 s autostop，exit 0 |
| Quilt | 0.20.0-beta.9 / 1.18.2 | 51（原版 37 + Quilt 14） | 进主菜单，35 s，exit 0 |
| Forge | 40.3.0 / 1.18.2 | 65（**不含原版 jar**） | 进主菜单，45 s，exit 0；仅离线预期的 Realms 授权失败日志 |
| NeoForge | 1.20.1 | —— | **阻塞**：`maven.neoforged.net` 在本网络不可达（curl 000）；1.20.1 资源 624.3 MiB 也超过 500 MB 授权线 |

测试：**131 passed / 0 failed**（新增合并/加载器测试）。

## 5. 踩到并修掉的三个坑

1. **Fabric profile 的库只有 `name + url`，没有 `downloads.artifact`** → classpath 组装曾整批跳过加载器库 → `ClassNotFoundException: KnotClient`。修复：classpath 接受"有 maven 基址"的库；加回归测试。
2. **Forge installer 需要 `launcher_profiles.json`** → 首次直接失败退出 1。修复：installer 前写最小 stub。
3. **Forge/NeoForge 1.17+ 用 BootstrapLauncher，原版 jar 不能再进 `-cp`** → 模块系统冲突：`module minecraft contains package … module _1._18._2 exports …`。修复：BootstrapLauncher 版本从 classpath 排除原版 jar（srg/module 层自己加载）；加回归测试。

## 6. 缺口

1. **NeoForge 未验证**：网络不可达 + 1.20.1 资源 624.3 MiB。需要你：放通 `maven.neoforged.net`，并授权下载 1.20.1 资源（> 500 MB 授权线）。
2. Quilt 元数据没有 stable 标记 → 默认会选到 beta；可按需用 `--loader-version` 固定。
3. 加载器升级/降级、mods 目录管理、依赖冲突提示属 M7/M8。
4. OptiFine / LiteLoader 按裁剪暂缓。
