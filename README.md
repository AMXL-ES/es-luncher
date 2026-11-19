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
```

## 文档

先读 [`PROGRESS.md`](PROGRESS.md)，再读 [`docs/INDEX.md`](docs/INDEX.md)。

## 许可

本项目以 GPL-3.0-or-later 发布，详见 [`LICENSE`](LICENSE) 与 [`docs/adr/0002-license-gpl3.md`](docs/adr/0002-license-gpl3.md)。
