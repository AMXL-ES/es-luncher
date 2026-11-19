# 环境实测报告 · GLauncher

> 采集时间：2026-11-19 21:0x（本地 UTC+8）｜采集方式：本机只读命令 + curl 实测，全部命令见文末。
> 本文件只记录**实测到的事实**；估算值单独标注，不许混写。下一次采集请新建小节并保留旧值（趋势比单点更有用）。

## 1. 系统与硬件

| 项 | 实测值 | 命令 |
|---|---|---|
| OS | Windows 11 25H2，build **26200**（注册表 `ProductName` 仍写 "Windows 10 Pro for Workstations"，是历史字符串） | `Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'` |
| 架构 | AMD64 | `$env:PROCESSOR_ARCHITECTURE` |
| 内存 | **15.7 GB** | `Win32_ComputerSystem.TotalPhysicalMemory` |
| 磁盘 C: | 余 **33.6 GB** / 总 181.9 GB | `Get-PSDrive` |
| 磁盘 D: | 余 **91.6 GB** / 总 146.5 GB（可写，已实测写删） | 同上 |
| 磁盘 E: | 余 **98.2 GB** / 总 146.5 GB | 同上 |

## 2. 工具链

| 工具 | 状态 | 备注 |
|---|---|---|
| git | **2.56.0.windows.4**（`D:\dev\git\cmd\git.exe`） | 全局 `user.name/email` 未配置 → 本仓库已设 repo-local 占位 `HP <hp@mc-launcher.local>`，可由用户改 |
| .NET SDK | **10.0.401**（唯一 SDK） | `dotnet --list-sdks` |
| JDK 21 | **21.0.13.0.2 LTS (Microsoft)**，`D:\dev\jdk-21`，可执行 | `D:\dev\jdk-21\bin\java.exe -version` |
| JDK 17 | **17.0.12 (Microsoft)**，`C:\Program Files\Microsoft\jdk-17.0.12.7-hotspot` | 题目基线漏了这一套，实测存在 |
| PATH 上的 `java` | **坏的**：Oracle javapath 直接崩，退出码 **0xC0000409**（-1073740791） | `where.exe java` 首条即它 → 启动器**禁止**按 PATH 找 java |
| JDK 8 / 25 | 缺失 | M4 需按需下载 Mojang 官方运行时 |
| Node / npm / pnpm | 缺失 | 选 .NET 路线后不需要 |
| Rust / cargo | 缺失 | 同上 |

## 3. 网络探活（curl.exe，无代理变量，首次命中，非并发）

| 目标 | 实测 | 解读 |
|---|---|---|
| `piston-meta.mojang.com/mc/game/version_manifest_v2.json` | **200 / 3.53 s** | 主源可用但偏慢 |
| `libraries.minecraft.net/` | 404 / 1.16 s | 根路径无索引，正常 |
| `resources.download.minecraft.net/` | 404 / 1.17 s | 同上 |
| `bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json` | **302 / 0.90 s** | 镜像在线，需跟随重定向 |
| `login.microsoftonline.com/` | 302 / 0.78 s | 可达（M3 用） |
| `api.minecraftservices.com/minecraft/profile` | **401 / 0.97 s** | 无 token 时的预期结果 |
| `api.modrinth.com/v2/tag/game_version` | 200 / 1.79 s | M8 用 |
| `api.curseforge.com/v1/games` | **403 / 2.00 s** | 需 API key（M8 前必须申请） |

- `HTTP_PROXY` / `HTTPS_PROXY` / `ALL_PROXY` / `NO_PROXY` 全为空 → 直连。
- 结论：官方渠道可用；镜像作为**回退**（比官方快约 4 倍，但默认仍走官方以保哈希一致）。

### 3.1 脚本化二次采集（`scripts/probe-network.ps1`，DNS 已热，共 8/8 符合预期）

| 目标 | Total | TTFB |
|---|---|---|
| mojang-manifest | 1429 ms | 859 ms |
| mojang-libraries | 1296 ms | 1296 ms |
| mojang-assets | 1704 ms | 1704 ms |
| bmclapi-manifest | **177 ms** | 177 ms |
| microsoft-login | 690 ms | 690 ms |
| minecraftservices-profile | 670 ms | 670 ms |
| modrinth-api | 1067 ms | 737 ms |
| curseforge-api | 865 ms | 865 ms |

> 两次采集差异（官方 manifest 3.53 s → 1.43 s）说明**单次延迟不可作为 SLA**；M1 的性能结论必须多次采样取中位数。
> 原始 JSON：`D:\mc-data\logs\net.json`

## 4. 依赖版本（2026-11-19 取自官方 registry）

| 包 | 最新稳定版 |
|---|---|
| Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent | **12.1.6** |
| Avalonia.Diagnostics | 11.3.23（**无 12.x 稳定版** → M0 不引入） |
| Electron | 45.4.1（未采用） |
| @tauri-apps/cli | 2.12.6（未采用） |
| Velopack | 1.2.161（M10 自动更新候选，`TODO(需验证)` .NET 10 兼容） |
| xunit / Microsoft.NET.Test.Sdk / xunit.runner.visualstudio | 2.9.3 / 17.14.1 / 3.1.4 |

## 5. 复现命令

```powershell
# 系统
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
Get-PSDrive -PSProvider FileSystem | Where-Object Name -in 'C','D','E'
# 工具链
cd D:\dev\mc-launcher; dotnet --list-sdks; git --version
& 'D:\dev\jdk-21\bin\java.exe' -version; where.exe java
# 网络（本机只有 Windows PowerShell 5.1，没有 pwsh 7）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\probe-network.ps1 -JsonOut D:\mc-data\logs\net.json
```
