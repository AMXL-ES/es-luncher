# 参与贡献

先读 [`AGENTS.md`](AGENTS.md)：那是本项目的红线与工作方式（核心层零 UI 依赖、无证据不算完成、先查再写）。

## 环境

| 需要 | 版本 |
|---|---|
| .NET SDK | 10.0.401+（`dotnet --list-sdks` 确认） |
| Windows | 10/11 x64（UI 与安装链路只在 Windows 验收） |
| PowerShell | 5.1（脚本按 5.1 写，UTF-8 **带 BOM**） |
| vpk（仅打包） | `dotnet tool install vpk --tool-path D:\dev\tools\vpk` |

## 构建与测试

```powershell
dotnet build EsLuncher.slnx -c Release     # 必须 0 警告（TreatWarningsAsErrors 已开）
dotnet test  EsLuncher.slnx -c Release     # 必须全绿
powershell -NoProfile -File scripts\verify-acceptance.ps1 -WithUi   # M0–M10 一键验收
```

## 代码约定

1. **业务逻辑写进 `src/EsLuncher.Core`**。UI（`EsLuncher.App`）只做编排与展示；Core 不许引用任何 UI 框架（有架构守卫测试）。
2. **关键路径必须有固定 fixture 单测**（真实 version.json 的最小样本），不许把解析逻辑 mock 掉。
3. **修 bug 先写一个会失败的回归测试**，再让它变绿。
4. 外部输入一律当敌意数据：SHA1 校验、防 zip slip、大小与重试上限。
5. 不实现任何绕过正版校验、伪造会话、账号共享的功能。

## 提交与 PR

- 提交信息用语义化前缀：`feat(core): ...` / `fix(ui): ...` / `docs: ...` / `chore: ...`。
- 一个 PR 只做一件事；附上「命令 + 输出摘要」证明改动可用（截图或测试输出）。
- 改动涉及不可逆决定（数据格式、许可证、分发方式）时，先在 issue 里讨论并写进 `docs/adr/`。

## 许可证

提交即表示同意以 **GPL-3.0-or-later** 授权你的贡献。移植 Prism / MultiMC 等 GPL-3.0 代码是允许的，但必须在文件头注明来源并在 `NOTICE` 登记。
