# M1 调研 · 版本元数据与下载引擎

> 采集时间：2026-11-19 21:4x ｜ 全部数字来自 `piston-meta.mojang.com` 实测（原始文件缓存在 `D:\mc-data\cache\probe\`）。
> 本文只记录**事实 + 待决问题**；实现方案与判据在进入 M1 后由你的裁剪决定补全。

## 1. 版本清单（`version_manifest_v2.json`）

| 事实 | 值 |
|---|---|
| 文件大小 | **279,726 B**（约 273 KB，单次 GET 可接受） |
| 顶层字段 | `latest`、`versions` |
| `latest` | `release=26.3`，`snapshot=26.4-snapshot-10` |
| 版本条目字段 | `id`、`type`、`url`、`time`、`releaseTime`、`sha1`、`complianceLevel` |
| 版本总数 | **925**：release 103、snapshot 761、old_beta 26、old_alpha 35 |

**结论**：`type` 有 4 个取值，UI 与筛选逻辑不能假设只有 release/snapshot；`old_alpha`/`old_beta` 属于"全版本"目标的一部分。`sha1` 是**该 version.json 自身**的摘要，不是游戏文件摘要。

## 2. 单个 version.json

| 事实 | 26.3（现代） | 1.7.10（legacy） |
|---|---|---|
| JSON 大小 | 45,056 B | 14,777 B |
| `mainClass` | `net.minecraft.client.main.Main` | 同 |
| 启动参数格式 | `arguments`（新旧双格式中的新格式） | `minecraftArguments`（字符串模板） |
| `javaVersion` | `{component: java-runtime-epsilon, majorVersion: 25}` | `{component: jre-legacy, majorVersion: 8}` |
| `assets` | `34` | `1.7.10`（虚拟/legacy 映射） |
| `assetIndex` | `size=597,235`、`totalSize=487,810,291`、带 sha1 | `totalSize=112,396,854` |
| 客户端 jar | 41,483,720 B（≈39.6 MiB） | 5,256,245 B |
| `libraries` | 114 个 | 33 个 |
| 带 `rules` 的库 | 61 个 | 见各库 |
| natives（classifiers） | **0** | 有（`natives-windows`） |
| `minimumLauncherVersion` | 21 | — |

**实测结论（重要）**：

1. **现代版本已不再用 `classifiers.natives-windows`**（26.3 为 0），但 legacy 版本仍在用 → M1/M2 必须同时实现"新式 natives 由 libraries 直接给"和"旧式 classifiers 解压"两条路径。
2. **`javaVersion` 在 1.7.10 上也存在**（`jre-legacy` / 8）→ M4 的 Java 选择可以完全由元数据驱动，不必硬编码版本表（但仍需技能库交叉验证）。
3. `libraries[0]` 在 legacy 里只有 `downloads` + `name`，**没有 `rules`** → 佐证"缺省 = 允许"的规则求值语义。
4. 一个现代版本的完整下载量（26.3）：**客户端 39.6 MiB + 库 111 MiB + 资源 465 MiB ≈ 587 MiB**，资源对象 **5,149 个**（索引文件本身 597,235 B；1.7.10 则是 686 条 / 674 个唯一对象 / 107 MiB）。
   → 下载引擎按"几百到几千个文件、百 MB 级"设计（并发、句柄、去重、续传），磁盘预算按 GB 算。

## 3. 与提示词第十一节坑位的对照

| 坑位 | 实测状态 |
|---|---|
| manifest 字段与 sha1 校验 | ✅ 字段已确认；sha1 是 version.json 的摘要 |
| 规则求值（缺省=允许、按 action 覆盖） | ✅ legacy 库无 rules 已实测；26.3 有 61 个带 rules 的库，可直接做 fixture |
| natives `${arch}` 替换 + per-instance 解压 | ⚠️ 仅 legacy 需要；26.3 无 natives |
| `inheritsFrom` | 原版 JSON 都无此字段（属加载器叠加，M6） |
| Java 选择 | ✅ `javaVersion.majorVersion` 现代/legacy 都有 |
| 资源索引（`assetIndex`） | 597 KB 一次下载，再按哈希前 2 位分目录 |

## 4. 已裁剪（2026-11-19 用户确认）

1. **natives 解压放在 M1**（旧版本 ≤1.12.2 需要；实测 1.0 就有 4 个 natives 库）。
2. 下载并发默认 **8**，可配置。
3. fixture：**26.3 + 1.12.2 + 1.7.10** 三份最小样本。
4. M1 验收面含 `esluncher fetch --version <id>` 纯 CLI（不启动游戏）。
5. 镜像降级：**单文件连续失败 3 次，或连接直接失败**即切镜像，可手动覆盖。

## 5. 尚未验证（标 `TODO(需验证)`）

- `complianceLevel` 的语义（是否影响启动）。
- 26.x 线 `assetIndex.id` 与旧版 `virtual/legacy` 资源的差异。
- 597,235 个资源对象的**实际磁盘占用**（按 4096B 簇算会远大于 465 MiB）→ 下载切片时实测一次。

## 6. 解析器对真实 JSON 的交叉验证（2026-11-19）

用临时控制台工具（已删除）让 `EsLuncher.Core` 直接吃 Mojang 原始文件：

| 文件 | libs | 带 rules | 带 natives | 参数格式 | javaVersion | client 字节 |
|---|---|---|---|---|---|---|
| manifest.json | — | — | — | — | — | 925 版本 / 4 种 type ✅ |
| 26.3 | 114 | 61 | 0 | `arguments` | 25 | 41,483,720 |
| 1.21.4 | 113 | 54 | 0 | `arguments` | 21 | 28,335,587 |
| 1.12.2 | 39 | 8 | 5 | `minecraftArguments` | 8 | 10,180,113 |
| 1.7.10 | 33 | 2 | 4 | `minecraftArguments` | 8 | 5,256,245 |
| 1.0 | 13 | 6 | 4 | `minecraftArguments` | 8 | 2,362,837 |

**新发现（修正前面的判断）**：natives 不是"只有 1.7.10 有"——**1.0 也有 4 个 natives 库**；而是
**1.13+ 改用 LWJGL3 的原生 jar 直接进 classpath**（如 26.3 的 `com.mojang:jtracy:...:natives-linux`，
以普通 library + rules 出现），所以现代版本 `natives`/`classifiers` 字段为 0。
→ M1 的解压路径只服务 ≤1.12.2；现代版本不需要解压，但**规则过滤必须对 natives jar 同样生效**（已实现）。

## 7. 已知的实现边界

- 解析层只做"读 + 判 + 拼字符串"，不碰网络与磁盘。
- `RuleEvaluator` 采用"缺省允许、命中的规则后者覆盖"：同时满足 26.3 的 `[allow osx]` 与 1.12.2 的 `[allow, disallow osx]`。
- 旧格式分词先分词后展开，因此占位符值里的空格不会被切坏（比 Mojang 原版更稳）。
- `${arch}` 在 natives 里取 **32/64**（实测 1.7.10 的 twitch 库用 `natives-windows-32/64`），与 rules 的 `os.arch`（x86/x86_64）不是同一套取值。
- 旧版存在"只有 natives、没有 artifact"的库（1.7.10 有 5 个），计划生成必须允许这种形状。

## 8. 下载引擎验收记录（2026-11-19）

| 项 | 结果 |
|---|---|
| dry-run 计划 | `esluncher fetch --version 1.7.10 --dry-run` → 36 个基础文件 / 26.0 MiB（client + 索引 + 日志 + 33 库），4 个 natives 解压目标 |
| 全量执行 | `esluncher fetch --version 1.7.10` → **exit 0，710 文件 / 133.1 MiB / 0 失败 / 237 s**（并发 8，官方源） |
| 资产 | 索引 686 条 → **674 个唯一对象**（12 个重复哈希被去重）/ 107 MiB |
| 独立复核（不用下载器代码） | 674 个对象逐个核对 size + SHA1：**0 bad / 0 missing**；client jar SHA1 与 version.json 一致 |
| natives | 14 个文件解压到 `instances/1.7.10/natives`（jinput-dx8.dll 等） |
| 幂等重跑 | **0.3 s，下载 0 / 跳过 710 / 失败 0** |
| 磁盘 | `D:\mc-data` = 158.3 MiB（数据根整体） |
| 镜像 | 本轮全走官方源成功；BMCLAPI 三个端点（maven/assets/version-client）探活均 206，路径由单测覆盖 |
| **未演练** | 真实断网后的续传：续传逻辑有单测（Range 206 / 服务端忽略 Range 两种），但没有真机断网实验 → `TODO(需验证)` |
