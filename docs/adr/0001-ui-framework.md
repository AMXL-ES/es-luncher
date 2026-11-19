# ADR 0001 · UI 框架与运行时选型

- **状态**：Accepted（2026-11-19，用户确认）
- **决策**：**.NET 10 + Avalonia 12.1.6**
- **影响范围**：整个工程；换壳成本被"引擎零 UI 依赖"红线压到最低

## 背景（实测，见 `docs/env-report.md`）

- 本机只有 .NET SDK **10.0.401** 与 git；**Node / Rust 全缺**。
- C 盘仅余 33.6 GB，D 余 91.6 GB → 新增工具链必须能装到 D 且体积可控。
- 凭据加密（DPAPI）、进程管理、HTTP、zip、SHA1 在现代 .NET 里都是一等公民；Node 侧 DPAPI 需要 keytar（原生模块，prebuild 风险）。
- 网络：官方 manifest 3.53 s、BMCLAPI 0.90 s、CurseForge 403（需 key）。

## 四路线对比

体积 / 内存 / 冷启动三项为**估算**（基于同类公开基准与经验），M0 冒烟后用 Avalonia 实测值替换本行；其余为实测或硬事实。

| 维度 | Electron 45.4.1 | Tauri 2.12.6 | **Avalonia 12.1.6** | JavaFX (JDK 21) |
|---|---|---|---|---|
| 分发体积（估算） | 安装包 90–120 MB，解压 ~200 MB | 3–10 MB | 自包含 50–70 MB；裁剪+AOT 35–50 MB | 40–70 MB（jlink） |
| 常驻内存（估算） | 150–250 MB | 60–120 MB | 80–150 MB | 150–250 MB |
| 冷启动（估算） | 1.5–3 s | 0.6–1.2 s | 0.5–1.0 s | 1.5–3 s（JVM 预热） |
| 开发迭代速度 | 最快（Web 生态） | 中（Rust 编译慢） | 中快（C# 热重启稍弱） | 中 |
| 下载/进程管理侧 | Node 主进程 | Rust 侧 | C# 侧 | Java 侧 |
| 自动更新 / 签名成熟度 | electron-updater 最成熟 | updater 插件较新 | Velopack 1.2.161 成熟 | jpackage，链路一般 |
| 本机需新增工具链 | Node 22+（~300 MB） | Rust + MSVC Build Tools（~2 GB） | **无** | 无（需 JavaFX SDK） |
| 最大长期风险 | 体积/内存被玩家诟病；Chromium 升级负担 | Rust 人力与编译成本；WebView2 版本碎片 | 控件生态小，精细 UI 手工多；12.x 生态尚新 | 打包分发痛苦；UI 表现力上限 |

## M0 实测（2026-11-19，替换上表 Avalonia 行的估算）

| 指标 | 实测 | 采集方式 |
|---|---|---|
| 冷启动（进程启动 → 主窗口可交互） | **933 ms / 696 ms**（两次，Release，framework-dependent） | `scripts/smoke-m0.ps1` 轮询 `MainWindowHandle` |
| 工作集 | **102.3 MB / 99.0 MB** | 同上，`WorkingSet64` |
| 单 exe 体积 | 0.2 MB（framework-dependent，依赖共享 .NET 运行时） | 同上 |
| 构建 | `dotnet build -c Release` → 0 warning / 0 error，52 s（含首次还原） | 构建输出 |

> 自包含 / 裁剪后的体积仍待 M10 实测；不要拿上面的 0.2 MB 对外宣称"启动器只有 200 KB"。

## 决策理由（最强一条）

**本机唯一装好的工具链就是 .NET**，而宪章第 5 条（DPAPI 凭据加密）与 M2（进程/参数管理）在 .NET 里是零摩擦实现；选 Electron/Tauri 要先装工具链、再解决 keytar/凭据与打包签名，等于在 33 GB 的 C 盘上给自己加两层风险。

## 代价（明确接受）

1. Avalonia 控件生态小于 Web：复杂主题、精细动效、富文本渲染要手工做，M7 预计多花 30–50% 工时。
2. 社区与第三方组件少，遇到怪问题可搜到的答案更少（GPL-3.0 下可移植 Prism 的 Java 逻辑，但 UI 层没有可抄的现成壳）。

## 与红线的兼容性

- 引擎 `GLauncher.Core` 是纯 `net10.0` 类库，Avalonia 只在 `GLauncher.App` → 壳可换。
- 跨平台：Avalonia 本身跨 Win/macOS/Linux；本机只验收 Windows。

## 何时重评

- Avalonia 12.x 出现阻断级控件缺陷，或 M7 阶段评估"UI 工时超预算 50%"成立时，再开 ADR 0003。
