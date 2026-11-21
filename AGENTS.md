# AGENTS.md · 给未来每一轮会话的契约

> 你（未来的我）在每个会话开局先读：`PROGRESS.md` → `docs/INDEX.md` → 当前里程碑文档。
> 不要重新通读历史代码；需要细节去读 `docs/`，不要靠回忆。

## 0. 30 秒上手（2026-11-21 更新）

| 项 | 值 |
|---|---|
| 远端仓库 | https://github.com/AMXL-ES/es-luncher （Public，GPL-3.0，`main`，CI 绿） |
| 当前进度 | M0–M11 完成；M12 待做（发行说明 / 许可证清单 / 回滚方案） |
| 最近发布 | v0.1.5（Velopack 包；`release.yml` 已改为 Velopack 管线） |
| 已知缺口 | 微软登录缺 Azure client ID、NeoForge 网络不可达、CurseForge 未接、真实代码签名证书未办 |

```powershell
cd D:\dev\es-luncher
dotnet build EsLuncher.slnx -c Release                                # 必须 0 警告
dotnet test  EsLuncher.slnx -c Release                                # 204 测试
powershell -File scripts\verify-acceptance.ps1 -WithUi -WithLaunch     # 17 项验收
powershell -File scripts\pack-velopack.ps1 -Version x.y.z              # 出包（vpk 在 D:\dev\tools\vpk）
```

**本机三个坑**：`.ps1` 必须 UTF-8 带 BOM（`.md` 不带）；PATH 上的 `java` 会崩（0xC0000409，永远用绝对路径）；Watt Toolkit 的「GitHub 加速」会对 github.com 做 MITM，`git push` 需 repo-local
`http.sslBackend=openssl` + `http.sslCAInfo=D:\dev\git-ca\ca-bundle+steamtools.crt`。

## 1. 项目身份

- **ES Luncher**：Minecraft Java 版桌面启动器，Windows 优先、架构跨平台。
- 技术栈：**.NET 10（SDK 10.0.401）+ Avalonia 12.1.6**；许可证 **GPL-3.0**。
- 工程根 `D:\dev\es-luncher`；运行数据根 `D:\mc-data`（**C 盘不落盘**，仅剩 ~33 GB）。
- 引擎 `src/EsLuncher.Core`：零 UI 依赖、可单测、可 CLI 复用。UI 只是壳。

## 2. 红线（违反即返工，不许找理由）

1. 核心层不许引用 Avalonia / `System.Windows` / 任何 UI 框架；UI 逻辑写进 App 层。
2. 不碰用户自己的 `.minecraft`；只有显式"导入现有安装"时**只读**。
3. 一切外部输入是敌意数据：哈希校验、解压路径限制（防 zip slip）、大小上限、重试上限。
4. **绝不**伪造 token / 绕过正版校验 / 账号共享；离线模式必须明确标注「仅本地游玩」。
5. 凭据用 DPAPI（或系统级等价物）加密落盘；绝不进日志、绝不进仓库、绝不明文。
6. 无证据不算完成：命令 + 退出码 + 输出摘要 + 产物路径，缺一不可。
7. 先查再写：API 字段、版本对应关系先实测/查官方；查不到标 `TODO(需验证)` 并告诉用户。
8. **禁止按 PATH 调用 `java`**：本机 PATH 上的 java 会崩（0xC0000409）。永远用解析出的绝对路径。

## 3. 目录约定

```
D:\dev\es-luncher\
  src/EsLuncher.Core/      纯引擎（无 UI）
  src/EsLuncher.App/       Avalonia 壳
  tests/EsLuncher.Core.Tests/
  docs/                    事实源：env-report / adr / 里程碑文档
  scripts/                 探活、冒烟、构建辅助
D:\mc-data\                运行数据：instances / libraries / assets / jdks / logs
```

## 4. 每轮工作流

1. 读 `PROGRESS.md` → `docs/INDEX.md` → 当前里程碑文档（三读，别通读代码）。
2. 动手前 `error_lessons_search`；修好新错误后 `error_lessons_add`。
3. 只推进一个里程碑；独立调研用 `subagent_fork` 并行，接口先冻结。
4. 完成 → 跑判据命令 → 更新 `PROGRESS.md` → commit → 按第九节格式汇报 → **停手等验收**。
5. 同一问题卡 3 轮就停手报告，不许连试。

## 5. 问与不问

- **必须问**：不可逆决策（数据格式、许可证、安全模型、破坏性重构）、要授权/花钱的、只有用户能提供的（Azure client ID、真实账号、UI 偏好）、上一轮话有歧义。
- **不许问**：能自己查的、有合理默认值的小事（写进 `docs/DECISIONS.md` 并标注"默认值，可推翻"）。
- 问法：`ask_user_question` 一次批量 3–8 个，首选项标 `(Recommended)` 并给取舍。
- 用户不答：按推荐项继续，假设写进 `docs/ASSUMPTIONS.md`，下轮汇报第一行提醒。

## 6. 代码与提交

- `TreatWarningsAsErrors` 已开：零警告才算过。
- 语义化 commit：`feat(core): ...` / `fix(core): ...` / `docs: ...` / `chore: ...`。
- 关键路径必须有固定 fixture 单测（真实 version.json 最小样本），不 mock 掉解析逻辑。
- Bug 修完先补一个会失败的重现测试，再让它变绿。

## 7. GPL-3.0 纪律

- 移植 Prism/MultiMC 代码**合法**，但必须：保持 GPL-3.0、文件头注明来源与原作者、登记进 `NOTICE`。
- 依赖必须是 GPL 兼容（MIT/BSD/Apache-2.0 可）；引入新许可证依赖前先确认并记录到 `docs/adr/0002-license-gpl3.md`。
- 不许把"参考了思路"说成"抄了代码"，反之亦然。
