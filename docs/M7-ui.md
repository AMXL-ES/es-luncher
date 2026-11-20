# M7 · 界面（进行中）

> 用户裁剪：图标**纯黑或纯白背景**，中部偏上大写 **ES**，下方 **luncher**；工作室（Endless Studio）的名称与图标**只出现在开发者/鸣谢页**。
> 本轮：图标 + UI 壳（实例页可用、关于页含工作室信息、下载/设置占位）。

## 1. 图标

| 文件 | 用途 |
|---|---|
| `assets/icon/es-luncher-dark-1024.png` / `-256.png` / `.ico` | 黑底白字（默认） |
| `assets/icon/es-luncher-light-1024.png` / `-256.png` / `.ico` | 白底黑字（浅色备用） |
| `scripts/gen-icon.ps1` | 可复现的生成脚本（System.Drawing，PS 5.1） |

- 视觉：纯黑/纯白背景；**ES** 用 Segoe UI Black，块面略高于中心；**luncher** 小写、宽字距，位于 ES 下方。
- 应用：`ApplicationIcon`（exe 图标）+ 窗口 `WindowIcon`（`avares://EsLuncher/Assets/…`）。

## 2. UI 壳（M7 第一刀）

![实例页](img/m7-instances.png)

- 左侧导航：**实例 / 下载 / 设置 / 关于**；底部 GPL 与版本。
- 实例页：读取 `InstanceStore`（真实数据），显示名称/版本/内存/账号/创建日期；**启动**按钮走 `LaunchService`（解析继承链 → 选 Java → 组命令 → 补 natives → 写日志），状态行回显进度与退出码；**打开目录**用资源管理器。
- 关于页：Endless Studio 图标 + 名称 + GPL/版本 + 里程碑摘要（工作室信息仅在此页出现）。
- 深色 Fluent 主题，窗口 1120×720。

## 3. 验收记录（2026-11-20）

| 项 | 结果 |
|---|---|
| 构建 | 0 警告 / 0 错误 |
| 窗口启动 | 标题 "ES Luncher"，尺寸 1134×758（含边框），进程存活 |
| 实例列表 | 真实读到 5 个实例（dev-two / Fabric 测试 / Forge 测试 / Quilt 测试 / 克隆 实例） |
| 截图 | `docs/img/m7-instances.png` |
| 启动按钮 | 已接线到 `LaunchService`（同一编排在 CLI 已验证）；**未做点击级自动化** |

## 4. M7 待办

1. 下载页：版本列表 + 加载器选择 + 进度条/速率/取消（复用 fetch 引擎）。
2. 设置页：数据根、Java 路径、内存默认值、主题（深/浅）、语言。
3. 关于页：鸣谢名单（Endless Studio 已就位）、第三方许可、构建信息。
4. 视觉：选中态从 Fluent 蓝改为单色灰；图标 16/32 尺寸清晰度；高 DPI 验证。
5. 无障碍：键盘可达、对比度、屏幕阅读器标签。
