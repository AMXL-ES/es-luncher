# M10 · 分发与自动更新（Velopack）

> 决策见 `adr/0003-distribution-velopack.md`。目标：一条命令出安装包/便携包，程序自己检查、下载、应用更新与增量。
> 结果：**Velopack 全链路跑通**，含「只给 delta 的源」也能完成升级的实测。

## 1. 组成

| 能力 | 落点 | 说明 |
|---|---|---|
| 更新服务（检查/下载/应用） | `Core/Updates/UpdateService.cs`、`Core/Updates/VelopackUpdateService.cs` | 接口不泄漏 Velopack 类型；源支持本地目录、URL、默认安装信息 |
| 安装/更新钩子 | `App/Program.cs`（`VelopackApp.Build().Run()` 第一位） | 安装、更新、卸载时 Velopack 会带参数回调本程序 |
| 无界面更新路径 | `App/HeadlessUpdate.cs` | `--update-test [--source] [--log] [--apply]`，输出 `key=value`，给自动化验收用 |
| CLI 验收面 | `esluncher update [--source] [--download] [--apply]` | 开发态明确拒绝并给理由，不假装成功 |
| 界面入口 | 「其他 → 更新」卡片（检查更新 / 下载并重启 / 状态行） | 中英词条已加；`ESLUNCHER_UPDATE_SOURCE` 可指到本地或内网源 |
| 打包 | `scripts/pack-velopack.ps1` | publish → `vpk pack`，同目录连续打包自动出 delta |
| 日志 | `数据根/logs/update.log` | 每次检查/下载/应用都留一行 |

## 2. 打包与发布

```powershell
# 一次性安装 vpk（不污染 PATH）
dotnet tool install vpk --tool-path D:\dev\tools\vpk

# 出 0.1.0（Setup.exe + Portable.zip + full.nupkg）
powershell -NoProfile -File scripts\pack-velopack.ps1 -Version 0.1.0

# 同目录再打 0.1.1 → 自动生成 delta
powershell -NoProfile -File scripts\pack-velopack.ps1 -Version 0.1.1
```

产物在 `artifacts\releases\`：`ESLuncher-win-Setup.exe`、`ESLuncher-win-Portable.zip`、`ESLuncher-<版本>-full.nupkg`、`ESLuncher-<版本>-delta.nupkg`、`releases.win.json`、`assets.win.json`。

> **不要清空这个目录**：增量包与历史 release 绑定；清空后只能走 full 包。

## 3. 验收记录（2026-11-21，实机）

| 步骤 | 命令/动作 | 结果 |
|---|---|---|
| 打包 0.1.0/0.1.1/0.1.2/0.1.3 | `pack-velopack.ps1 -Version <v>` | 每次都 exit 0；Setup 21.4 MB、Portable 14.0 MB、full 14,049 KB |
| 增量生成 | 同上（同目录连续打包） | 0.1.1 delta **15.3 KB**、0.1.3 delta **37.1 KB**（对比 full 14,049 KB） |
| 静默安装 | `ESLuncher-win-Setup.exe --silent` | exit 0；装到 `%LocalAppData%\ESLuncher`，含 `current\`、`packages\`、`Update.exe` |
| 安装态自检 | `current\EsLuncher.exe --update-test --source <releases>` | `installed=True`、`current=0.1.0`、`available=none` |
| **在线升级（full 源）** | `--update-test --apply` | 0.1.0 → 0.1.1，重启后 `current=0.1.1`、`available=none` |
| **增量升级（只给 delta 的源）** | 源目录只留 `releases.win.json`/`assets.win.json`/`RELEASES`/`ESLuncher-0.1.3-delta.nupkg` | 0.1.2 → **0.1.3 成功**，`delta=True`，`downloaded=yes`，重启后 `current=0.1.3` |
| 卸载 | `Update.exe uninstall --silent` | 安装目录整体移除（延迟 3 秒 rmdir），无残留 |
| 开发态拒绝 | `esluncher update --source <releases>` | exit 2，「当前程序不是安装形态…请用 Setup.exe 安装后再试」 |

**关键判据**：增量升级那一行用的是**只含 delta、不含 full 包**的源目录。若走 full 包，14 MB 的包根本不在源里，升级不可能成功。

## 4. 已知缺口

1. **未签名**：SmartScreen 仍提示未知发布者；SignPath/Azure 方案见 `CODE-SIGNING.md`、`SIGNPATH-SETUP.md`。
2. **更新源还是本地目录**：GitHub Releases 源等仓库建好再接（`VelopackUpdateService` 已支持传 URL）。
3. **只做手动检查**：启动时自动检查与静默更新未接（避免开机弹窗打扰，留到有远程源后再做）。
4. **Inno 脚本退役**：`installer\es-luncher.iss` 与 `PACKAGING.md` 的 Inno 段落只作历史参考，不再维护。
5. 未验证：ARM64、MSI（`vpk pack --msi`）、企业静默部署参数矩阵。
