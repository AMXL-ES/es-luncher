# ES Luncher

轻量、开源、无广告的 Minecraft Java 版启动器（Windows 优先，架构跨平台）。

- **技术栈**：.NET 10 + Avalonia 12.1.6
- **许可**：GPL-3.0-or-later
- **状态**：0.1.0（可预览发布；安装包/自更新/微软登录在后续版本）

## 功能

- 版本下载（官方 / BMCLAPI 镜像）、Java 运行时管理、离线启动
- 实例管理：创建/克隆/导入导出/分组/内存与 Java 设置
- 加载器：Fabric / Quilt / Forge
- 内容管理：模组 / 资源包 / 光影 / 世界 / 数据包 / 截图
- Modrinth：浏览（分类/排序/图标）、详情、按类型安装、整合包建实例
- 诊断：日志错误、崩溃报告、提示、诊断包、缺失前置补装
- 界面：主页实例展示、下载分页、账户（离线）、中英界面（语言切换需重启）

## 运行要求

- Windows 10/11 x64
- 安装包：自带运行时，无需额外安装
- 便携 zip（framework-dependent）：需要 **.NET 10 Desktop Runtime**

## 快速开始

1. 安装 `ESLuncher-0.1.0-setup.exe`，或解压便携 zip 运行 `EsLuncher.exe`
2. 「下载」页选版本 → 下载并建实例
3. 「实例」页选中实例 → 启动
4. 首次启动会在 `%APPDATA%\ESLuncher` 建数据目录（可用环境变量 `ESLUNCHER_DATA` 指定）

> 离线账户在「账户」窗添加；微软登录后续版本提供。

## 开发构建

```powershell
cd D:\dev\es-luncher
dotnet build EsLuncher.slnx -c Release
dotnet test EsLuncher.slnx
powershell -NoProfile -File scripts\smoke-ui.ps1
```

发布便携包 / 安装包见 [docs/PACKAGING.md](docs/PACKAGING.md)。

## 文档

- [发布前检查](docs/RELEASE-CHECK.md)
- [打包说明](docs/PACKAGING.md)
- [已修/遗留问题](docs/ISSUES.md)
- [API 凭据申请](docs/API-KEYS.md)
- 其余里程碑文档见 [docs/INDEX.md](docs/INDEX.md)

## 许可

GPL-3.0-or-later，见 [LICENSE](LICENSE)。
