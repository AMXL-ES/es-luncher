# GLauncher

Minecraft Java 版桌面启动器（Windows 优先，架构跨平台）。

- **技术栈**：.NET 10 + Avalonia 12.1.6
- **许可证**：GPL-3.0-or-later
- **状态**：M0（环境 + 骨架）阶段，尚无业务功能
- **引擎**：`src/GLauncher.Core`（零 UI 依赖），UI 壳：`src/GLauncher.App`

## 构建

```powershell
cd D:\dev\mc-launcher
dotnet build GLauncher.slnx -c Release    # 零警告
dotnet test                               # 引擎单测
powershell -NoProfile -File scripts\smoke-m0.ps1     # 空窗口冒烟 + 冷启动毫秒数
powershell -NoProfile -File scripts\probe-network.ps1 # 网络探活

# M1 验收面：把某个版本完整下到数据根（默认 D:\mc-data）
.\src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe fetch --version 1.7.10 --dry-run
.\src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe fetch --version 1.7.10

# M2 验收面：离线启动（Java 按 version.json 要求自动选，绝不走 PATH）
.\src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe java
.\src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe launch --version 1.18.2 --dry-run
.\src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe launch --version 1.18.2 --username Dev --autostop 30

# M3 验收面：账号（DPAPI 加密落盘）与微软设备码登录
.\src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe login --provider offline --username Dev
.\src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe accounts
.\src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe launch --version 1.18.2 --account dev --autostop 30
.\src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe login --provider microsoft --client-id <azure-client-id>

# 版本列表 + 一条命令回归（M1–M3）
.\src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe versions --limit 10
.\src\GLauncher.Cli\bin\Release\net10.0\glauncher.exe versions --type snapshot --limit 5
powershell -NoProfile -File scripts\smoke-m1m3.ps1
```

## 文档

先读 [`PROGRESS.md`](PROGRESS.md)，再读 [`docs/INDEX.md`](docs/INDEX.md)。

## 许可

本项目以 GPL-3.0-or-later 发布，详见 [`LICENSE`](LICENSE) 与 [`docs/adr/0002-license-gpl3.md`](docs/adr/0002-license-gpl3.md)。
