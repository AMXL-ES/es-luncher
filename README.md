# ES Luncher

Minecraft Java 版桌面启动器（Windows 优先，架构跨平台）。

- **技术栈**：.NET 10 + Avalonia 12.1.6
- **许可证**：GPL-3.0-or-later
- **状态**：M0（环境 + 骨架）阶段，尚无业务功能
- **引擎**：`src/EsLuncher.Core`（零 UI 依赖），UI 壳：`src/EsLuncher.App`

## 构建

```powershell
cd D:\dev\es-luncher
dotnet build EsLuncher.slnx -c Release    # 零警告
dotnet test                               # 引擎单测
powershell -NoProfile -File scripts\smoke-m0.ps1     # 空窗口冒烟 + 冷启动毫秒数
powershell -NoProfile -File scripts\probe-network.ps1 # 网络探活

# M1 验收面：把某个版本完整下到数据根（默认 D:\mc-data）
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe fetch --version 1.7.10 --dry-run
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe fetch --version 1.7.10

# M2 验收面：离线启动（Java 按 version.json 要求自动选，绝不走 PATH）
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe java
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe launch --version 1.18.2 --dry-run
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe launch --version 1.18.2 --username Dev --autostop 30

# M3 验收面：账号（DPAPI 加密落盘）与微软设备码登录
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe login --provider offline --username Dev
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe accounts
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe launch --version 1.18.2 --account dev --autostop 30
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe login --provider microsoft --client-id <azure-client-id>

# 版本列表 + 一条命令回归（M1–M3）
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe versions --limit 10
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe versions --type snapshot --limit 5
powershell -NoProfile -File scripts\smoke-m1m3.ps1

# M4 验收面：Mojang 官方 Java 运行时（不装系统 JDK、不改 PATH）
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe java
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe java install --version 1.7.10
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe launch --version 1.7.10 --username Dev --autostop 30

# M5 验收面：实例（共享库/资源，独立配置与游戏目录）
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe instance create "测试 实例" --version 1.18.2 --memory-mb 1536
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe instance list
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe launch --instance "测试 实例" --autostop 30
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe instance export "测试 实例" --out D:\backup\pack.zip
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe instance import D:\backup\pack.zip "还原 实例"

# M6 验收面：模组加载器（Fabric/Quilt/Forge 已真机验证）
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe loader list --version 1.18.2 --loader fabric
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe instance create "Fabric 测试" --version 1.18.2 --loader fabric
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe fetch --version fabric-loader-0.20.1-1.18.2 --only libraries
.\src\EsLuncher.Cli\bin\Release\net10.0\esluncher.exe launch --instance "Fabric 测试" --autostop 30
```

## 开发者

想基于 `EsLuncher.Core` 写自己的启动器：见 [`docs/DEVELOPER-GUIDE.md`](docs/DEVELOPER-GUIDE.md)，以及可直接运行的 [`samples/MinimalLauncher`](samples/MinimalLauncher)。

## 文档

先读 [`PROGRESS.md`](PROGRESS.md)，再读 [`docs/INDEX.md`](docs/INDEX.md)。

## 许可

本项目以 GPL-3.0-or-later 发布，详见 [`LICENSE`](LICENSE) 与 [`docs/adr/0002-license-gpl3.md`](docs/adr/0002-license-gpl3.md)。
