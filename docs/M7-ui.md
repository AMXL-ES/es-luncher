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

## 4. M7 第二刀（下载页 / 设置页 / 关于页）

![下载页](img/m7-downloads.png)

![设置页](img/m7-settings.png)

![关于页](img/m7-about.png)

| 页面 | 能力 | 说明 |
|---|---|---|
| 下载 | 版本清单（925 条，正式/快照/全部筛选）、刷新清单、下载选中（进度条 + 文件数/字节 + 取消） | 复用 `VersionFetcher`/`DownloadExecutor`；实际下载走同一套 SHA1 校验 |
| 设置 | 数据根展示、默认内存、Java 路径、深/浅主题、保存 | `LauncherSettings` 落盘 `launcher-settings.json`；主题保存即生效 |
| 关于 | Endless Studio 图标+名称、GPL、版本、里程碑摘要 | 工作室信息仅此页出现 |

- 验收：构建 0 警告、**134 tests 全绿**；三个页面均实机截图（`ESLUNCHER_UI_PAGE` 作为验收入口，仅开发用）。
- 修掉的坑：XAML 加载期 `IsSelected` 先触发、命名控件尚未赋值 → `OnNavChanged` 空引用；内联 `IsSelected` 会在构造后再改回"实例"页 → 去掉内联选中，统一在构造里设置。

## 5.5 大改版：对标 PCL2/HMCL 的布局（2026-11-20）

![主页](img/m7-home.png)

![实例页](img/m7-instances.png)

- **顶栏导航**：Logo + ES Luncher + 主页/实例/下载/设置/关于（替代左侧栏）
- **主页**：选中实例大卡（首字母图标、名字、版本、内存/分组）+ 蓝色「启动游戏」主按钮 + 快捷操作（打开目录/内容管理/诊断/Modrinth/日志）+ 实例总数
- **实例页**：2 列卡片网格（首字母图标/名字/版本/分组）+ 搜索/分组；右侧 430px 固定内容面板（类型、增删、启停、下载、检查更新、备份世界）
- **样式**：`Button.primary`（蓝）/`Button.ghost`（灰）/`Button.nav` 三套，卡片圆角 12，状态栏底部常驻
- 保留全部既有功能与验收钩子（`ESLUNCHER_UI_PAGE=home|instances|downloads|settings|about`）

### 账户登录（UI，2026-11-20）

![账户窗口](img/m7-accounts.png)

- 「账户」窗口：列出已保存账户（名字/类型/当前实例标记），**使用**（绑定到当前实例）、**删除**
- 账户头像：优先从 crafatar 取皮肤头（`id` 是真实账号 UUID 时），失败回退首字母
- **添加离线账户**：输入名字即保存（DPAPI）
- **微软登录（设备代码）**：显示验证 URL + 代码，等待授权；需要 `ESLUNCHER_MSA_CLIENT_ID`（Azure 公共客户端）
- 「取消等待」可中断微软授权轮询

### 第二批（下载/设置/账户/最近实例，无动画）

- **下载页改为分页**：`游戏版本 / 整合包 / 模组 / 资源包 / 光影 / 数据包` 六个 Tab
  - 顶部统一「目标实例 + 世界（数据包）」上下文，自动推导 MC 版本与加载器
  - 模组/资源包/光影/数据包：Modrinth 搜索 + **项目图标 + 下载量**（HMCL 风列表）+ 一键装进选中实例
  - 整合包：搜索 + 填实例名 + 直接建实例（`ModrinthDownloadPanel` 复用）
- **主页最近实例**：横向卡片，点击直接切换选中实例
- **账户栏**：顶栏右侧头像 + 名称（离线/已保存账户），点击打开「账户」窗口列出已保存账户
- **设置页**：分区卡片（常规 / 外观与语言 / Java / 保存）
- 全部静态样式，无动画、无过渡

### 打磨细节

- 窗口 1280×800（最小 1000×640），实例工具栏与搜索框分行，长内容不再挤出窗口
- 实例**搜索框**（名字/版本过滤）+ **分组下拉**（`instance group <名字> <分组>`），卡片显示分组
- 关于页补第三方组件（Avalonia MIT / .NET 10 MIT）与构建信息
- 设置页加「打开数据目录」与「语言（中文/English，重启生效）」
- 诊断窗错误行支持**搜索过滤**、自动换行；窗口加高保证底部按钮可见
- 内容面板按类型显示按钮（检查更新 / 备份世界 / 上移下移），不再堆一排无关按钮

### 关于页 · 鸣谢表

![关于页](img/m7-about.png)

- 上方：工作室图标 + **Endless Studio**（按约定只在关于页出现）
- 下方：**开发者 / 鸣谢** 表（名字 / 角色），数据来自 `src/EsLuncher.App/Assets/credits.json`
- 当前名单：`AMXL_`（开发者）、`大肥鱼`（AI 助手）
- **可扩展**：直接编辑 `credits.json` 增删条目并重新构建，不需要改代码

### i18n 基础框架

![English UI](img/m7-i18n-en.png)

- `Localization.T(key)` + 中英词表；覆盖导航、实例工具栏、搜索占位、标题
- 语言存 `launcher-settings.json`，重启生效；`ESLUNCHER_LANGUAGE=en` 可直接覆盖（验收用）
- 其余页面文案仍是中文：加词条只需在 `Localization.cs` 补一行并替换控件

## 6. M7 待办（剩余）

1. 下载页：加载器选择（Fabric/Quilt/Forge）与安装后实例化。
2. 设置：默认内存/Java 路径写入实例创建默认值；语言切换（i18n）。
3. 视觉：高 DPI 验证、16/32 图标清晰度、主题色 token 化。
4. 无障碍：键盘可达、对比度、屏幕阅读器标签。
5. 关于页：第三方许可清单、构建信息。
6. 启动按钮点击级自动化（可用 Avalonia Headless 或 UI 自动化补）。
