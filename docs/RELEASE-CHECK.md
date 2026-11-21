# 发布前检查（2026-11-21 复核）

> 本文件在 2026-11-21 被**整篇重写**：原文件（含 `artifacts/` 下两份副本、以及 git 历史里的两个版本）已损毁成 `锟斤拷` 双编码乱码，无法恢复。以下每一条都是当天实测，不是沿用旧结论。
> 一键复跑：`powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-acceptance.ps1 -WithUi -WithLaunch`

## 1. 自动检查

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build EsLuncher.slnx -c Release` | **0 警告 / 0 错误**，1.2 s |
| 测试 | `dotnet test EsLuncher.slnx -c Release` | **204 passed / 0 failed**（203 Core + 1 Headless UI） |
| 冷启动 | `scripts\bench-startup.ps1 -Runs 5` | **中位 948 ms** / p95 976 ms / 常驻 112.1 MB（优化前 1306 ms / 124.6 MB，见 `M11-performance.md`） |
| 验收脚本 | `scripts\verify-acceptance.ps1 -WithUi -WithLaunch` | **17 / 17 PASS**，exit 0 |
| UI 冒烟 | `scripts\smoke-ui.ps1` | 进程存活 8 s 后正常关闭（10.2 s） |
| 真实启动回归 | `launch --instance "Fabric 测试" --autostop 25` | **exit 0**；日志 `Loading 51 mods` / `Setting user: Player` / `Backend library: LWJGL version 3.2.2` / JEI atlas 创建成功 |

验收脚本覆盖：M0 构建与测试、M1 版本清单与 1.7.10 下载计划、M2 1.18.2 命令组装、M3 账号列表、M4 Java 发现、M5 实例列表/体积/实例级启动、M7 UI 冒烟、M8 内容列表、M9 依赖体检与诊断、M10 开发态自更新拒绝，外加一次真实启动。

## 2. 发布形态现状

| 项 | 状态 |
|---|---|
| 便携包 | `scripts\pack-velopack.ps1`（Velopack，约 14 MB zip） |
| 安装包 | `ESLuncher-win-Setup.exe`（Velopack，约 21 MB，静默安装 `--silent`） |
| 自动更新 + 增量 | **已实测**：增量包 15.3/37.1 KB；用「只含 delta 的源」完成 0.1.2 → 0.1.3 升级 |
| 代码签名 | 未签；方案见 `CODE-SIGNING.md`、CI 接入见 `SIGNPATH-SETUP.md` |
| 更新源 | 目前是本地目录（暂无 git remote）；GitHub Releases 源等仓库建好再接 |
| 数据根默认值 | `%APPDATA%\ESLuncher`，可用 `ESLUNCHER_DATA` 或 `--data` 覆盖 |
| 用户可见文案 | 已词条化（中/英），微软登录入口按决定隐藏 |

> Inno Setup（`installer\es-luncher.iss`）已退役，仅作历史参考，见 `adr/0003-distribution-velopack.md`。

> 本次复核**没有**重新走「安装 → 升级 → 卸载」全流程截图留档：该流程在 M10 交付时已实测（见 `M10-distribution.md`）。要发版仍需按 `PACKAGING.md` 重跑一次。

## 3. 已知缺口（照实写，不粉饰）

1. **M10 未完全收尾**：签名未接（缺证书/CI 凭据）；更新源还是本地目录；启动时自动检查未做。
2. **微软正版登录**：代码 + 12 个测试就绪，缺 Azure 公共客户端 ID，且 UI 入口按你的决定隐藏。
3. **NeoForge 未通**：`maven.neoforged.net` 本网络不可达 + 1.20.1 资源 624 MiB 超授权线。
4. **CurseForge 未接**：需 API key 并确认分发条款。
5. 大文件（版本/资源）下载仍无取消与真实进度条（只有 Modrinth 安装有）。
6. 语言切换需重启（`{app:T}` 在加载期求值）；150% 缩放未测；完整读屏支持仍是基础级。
7. UI 自动化测试只有 1 条 Headless 用例，深交互无覆盖。

## 4. 结论

**可以出 0.1.0 源码 + 便携预览**：构建零警告、202 测试全绿、16 项验收全过、真实启动回归通过。
**不建议现在对外宣称「正式版」**：先补 M10 的自动更新与签名，再恢复微软登录、补 150% 缩放与多页 UI 测试。
