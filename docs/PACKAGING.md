# 打包说明（Velopack，2026-11-21 起）

> 分发决策见 `adr/0003-distribution-velopack.md`。Inno Setup 那套脚本已退役，仅作历史参考（见文末）。

## 前置

- .NET SDK 10.0.401（已装）
- `vpk` CLI（装在项目外，不污染 PATH）：
  ```powershell
  dotnet tool install vpk --tool-path D:\dev\tools\vpk
  ```

## 1. 出包

```powershell
cd D:\dev\es-luncher

# 常规（framework-dependent，约 14 MB 包 / 21 MB 安装器）
powershell -NoProfile -File scripts\pack-velopack.ps1 -Version 0.1.0

# 自包含（用户机器不需要 .NET 运行时，包更大）
powershell -NoProfile -File scripts\pack-velopack.ps1 -Version 0.1.0 -SelfContained

# 不要增量（只出 full）
powershell -NoProfile -File scripts\pack-velopack.ps1 -Version 0.1.0 -NoDelta
```

产物在 `artifacts\releases\`：

| 文件 | 用途 |
|---|---|
| `ESLuncher-win-Setup.exe` | 安装包（静默安装：`--silent`） |
| `ESLuncher-win-Portable.zip` | 便携包 |
| `ESLuncher-<版本>-full.nupkg` | 完整更新包 |
| `ESLuncher-<版本>-delta.nupkg` | 增量更新包（从上一个 release 差分） |
| `releases.win.json` / `assets.win.json` / `RELEASES` | 更新源索引 |

> **发布目录要长期保留**：清空后只能发 full 包，用户失去增量。
> 第二个版本开始，把新版本打进**同一个** `artifacts\releases`，vpk 才会生成 delta。

## 2. 签名（链路已接通，缺证书）

`scripts\pack-velopack.ps1` 已支持三种签名方式（细节见 `CODE-SIGNING.md`）：

```powershell
# PFX 直接签（不需要 signtool / Windows SDK）
powershell -NoProfile -File scripts\pack-velopack.ps1 -Version 0.1.4 `
    -PfxPath D:\dev\test-signing\es-luncher-test.pfx -PfxPassword es-luncher-test

# signtool 参数（证书装在证书存储里）
... -SignParams "/fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /n \"Endless Studio\""

# Azure Trusted Signing
... -AzureTrustedSignFile D:\tools\metadata.json
```

自签证书已实测能把 Setup.exe、Portable 里的 `current\EsLuncher.exe`、nupkg 里的 `lib/app/EsLuncher.exe` 全部签上（带 DigiCert 时间戳），
但自签名根不受信，**SmartScreen 依旧提示**。要真正受信必须先申请证书（见 `CODE-SIGNING.md` 的方案表）。

## 3. 发布前最后检查

```powershell
dotnet build EsLuncher.slnx -c Release      # 0 警告 0 错误
dotnet test EsLuncher.slnx                   # 全绿
powershell -NoProfile -File scripts\verify-acceptance.ps1 -WithUi -WithLaunch
```

再手工确认三件事：

1. `Setup.exe --silent` 装完能开、能启动一个实例；
2. 用 `--update-test --source artifacts\releases` 验证 `current` 等于刚打的版本；
3. 卸载干净（`%LocalAppData%\ESLuncher` 整体消失）。

## 4. 历史：Inno Setup（已退役）

`installer\es-luncher.iss`、`scripts\publish-portable.ps1` 保留但不维护。
旧流程（`ISCC.exe installer\es-luncher.iss`）只在需要「不依赖 Velopack 的裸安装包」时参考，
两者不要同时装到同一台机器（会出双份快捷方式和两套更新器）。
