# PROGRESS · GLauncher

> 唯一事实源：本文件回答「现在在哪」。历史细节进 `docs/`，决策进 `docs/adr/`，默认值进 `docs/DECISIONS.md`。
> **每轮开局三读**：本文件 → `docs/INDEX.md` → 当前里程碑文档。

- **目标 id**：`goal-db5cdd51-065f-4a37-a25d-883614f0ab16`
- **当前阶段**：**M1 进行中**：元数据解析层完成（33 个测试全绿），下载引擎未开始（2026-11-19）
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
- 现代版本 26.3 完整下载量 ≈ **587 MiB**（client 39.6 + libs 111 + assets 465），资源对象 **597,235 个** → 下载引擎按"百万级小文件"设计
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

## 下一步（M1 剩余：下载引擎）

按已裁剪决定做：并发 8（可配）、SHA1 全校验、断点续传、镜像降级（失败 3 次或连接失败即切）、assets 全量、natives 解压（仅 ≤1.12.2）、`glauncher fetch --version <id>` CLI 验收面。
