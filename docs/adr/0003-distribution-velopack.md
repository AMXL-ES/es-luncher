# ADR 0003 · 分发与自动更新：Velopack 全量接管

- **状态**：Accepted（2026-11-21，用户确认「Velopack 全量接管 / 先本地目录源」）
- **决策**：用 **Velopack 1.2.161** 同时承担安装包、便携包、自动更新与增量更新；Inno Setup 那套脚本退役（文件保留作历史，不再维护）
- **影响范围**：打包链路、应用启动入口、安装位置、发布流程

## 背景（实测，2026-11-21）

| 事实 | 证据 |
|---|---|
| Velopack 1.2.161 原生带 `lib/net10.0` | 解包 nupkg：`lib/net10.0/Velopack.dll` + `net472/net8.0/net9.0/netstandard2.0` |
| 许可证 | nuspec `license type="expression"` = **MIT**（与 GPL-3.0 兼容） |
| 依赖 | net10.0 目标组**零依赖**（net472 才要 Newtonsoft.Json） |
| `vpk` CLI 可用 | `dotnet tool install vpk --tool-path D:\dev\tools\vpk` → 1.2.161 |
| 生成增量 | 0.1.3 的 delta 仅 **37.1 KB**，同版本 full 是 14,049 KB |
| 仓库暂无 git remote | `git remote -v` 为空 → 更新源必须先取本地目录，GitHub 发布时再接 |

## 选项对比

| 方案 | 安装包 | 自动更新 | 增量 | 代价 |
|---|---|---|---|---|
| **Velopack 1.2.161** | Setup.exe | 有 | 有（默认 BestSpeed） | 安装到 `%LocalAppData%`；需保留历史 release 才能出增量 |
| Inno + 自研更新 | Inno | 自己写 | 无 | 要自己写下载/替换/回滚，最容易出「更新到一半废掉」 |
| Squirrel.Windows | Setup.exe | 有 | 有 | 维护活跃度低，.NET 10 支持需自行验证 |
| MSIX / AppX | MSIX | 有 | 有 | 需要证书与商店链路，个人开源项目门槛高 |

## 决策理由（最强一条）

Velopack 是唯一同时满足「.NET 10 原生支持、MIT、零依赖、自带增量」的方案，且它把最容易写坏的**替换正在运行的程序**这件事做成了成品；自己写这一段属于给项目引入最高风险的代码。

## 后果（明确接受）

1. 安装位置改为 `%LocalAppData%\ESLuncher`（Velopack 默认），**数据根不变**（`%APPDATA%\ESLuncher` / `ESLUNCHER_DATA`），升级不丢实例与下载。
2. 应用入口必须先 `VelopackApp.Build().Run()`，否则安装/更新/卸载钩子不执行。
3. 未签名时 SmartScreen 仍会提示未知发布者（签名方案见 `CODE-SIGNING.md`）。
4. 增量依赖「历史 release 与已安装版本对得上」；发布目录要长期保留，不能每次清空。

## 何时重评

- 需要企业 MSI 批量部署、或需要 ARM64 安装包而 Velopack 支持不足时；
- Velopack 停止维护，或 .NET 线不再被其覆盖时 → 届时另开 ADR。
