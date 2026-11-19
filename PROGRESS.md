# PROGRESS · GLauncher

> 唯一事实源：本文件回答「现在在哪」。历史细节进 `docs/`，决策进 `docs/adr/`，默认值进 `docs/DECISIONS.md`。
> **每轮开局三读**：本文件 → `docs/INDEX.md` → 当前里程碑文档。

- **目标 id**：`goal-db5cdd51-065f-4a37-a25d-883614f0ab16`
- **当前阶段**：**M0 完成待验收；M1 调研完成、未开工**（2026-11-19）
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

## 下一步（等你放行 M1）

先问 5 个裁剪问题 → 冻结 M1 接口 → 实现 manifest/version 解析 + 下载引擎 → CLI 验收 → 停手。
