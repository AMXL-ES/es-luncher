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
4. 一个现代版本的完整下载量（26.3）：**客户端 39.6 MiB + 库 111 MiB + 资源 465 MiB ≈ 587 MiB**，资源对象 **597,235 个**。
   → 下载引擎必须按"百万级小文件"设计（并发、句柄、去重、续传），并且磁盘预算要按 GB 算。

## 3. 与提示词第十一节坑位的对照

| 坑位 | 实测状态 |
|---|---|
| manifest 字段与 sha1 校验 | ✅ 字段已确认；sha1 是 version.json 的摘要 |
| 规则求值（缺省=允许、按 action 覆盖） | ✅ legacy 库无 rules 已实测；26.3 有 61 个带 rules 的库，可直接做 fixture |
| natives `${arch}` 替换 + per-instance 解压 | ⚠️ 仅 legacy 需要；26.3 无 natives |
| `inheritsFrom` | 原版 JSON 都无此字段（属加载器叠加，M6） |
| Java 选择 | ✅ `javaVersion.majorVersion` 现代/legacy 都有 |
| 资源索引（`assetIndex`） | 597 KB 一次下载，再按哈希前 2 位分目录 |

## 4. 待你裁剪的 5 个问题（进入 M1 前会正式问你）

1. M1 是否包含 **natives 解压**？（放 M2 也行，但 M1 的下载引擎会少一条真实路径的验收）
2. 下载并发默认值：**8**（可配）是否可接受？
3. fixture 范围：只存 26.3 + 1.7.10 两个最小样本，还是再加一个"带 natives 的中间版本"（如 1.12.2）？
4. 是否要 `glauncher fetch --version 26.3` 这种**纯 CLI 入口**作为 M1 验收面（不启动游戏）？
5. 镜像降级策略：**连续失败 3 次切镜像**，还是按域名固定映射（manifest→BMCLAPI，assets→官方）？

## 5. 尚未验证（标 `TODO(需验证)`）

- `complianceLevel` 的语义（是否影响启动）。
- 26.x 线 `assetIndex.id` 与旧版 `virtual/legacy` 资源的差异。
- 597,235 个资源对象的**实际磁盘占用**（按 4096B 簇算会远大于 465 MiB）→ M1 要实测一次。
