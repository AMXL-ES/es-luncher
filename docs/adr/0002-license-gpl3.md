# ADR 0002 · 许可证：GPL-3.0-or-later

- **状态**：Accepted（2026-11-19，用户确认"现在就全开源，GPL-3.0"）
- **决策**：项目整体以 **GPL-3.0-or-later** 发布，`LICENSE` 已放入 GNU GPL v3 原文。

## 这条决策换来了什么

**合法移植 Prism Launcher / MultiMC 的实现**（两者均为 GPL-3.0）。版本解析、加载器安装、`inheritsFrom` 叠加、natives 处理这些最容易写错的部分，可以读其源码后移植，而不是从零试错。M1/M2/M6 的工期风险因此显著下降。

## 必须遵守的义务

1. 任何移植/改写的 Prism、MultiMC 或其他 GPL-3.0 代码，保留原作者版权声明，并在文件头注明来源仓库 + 文件 + 提交号。
2. 所有移植条目登记进仓库根 `NOTICE`（M1 起建立）。
3. 整体继续以 GPL-3.0-or-later 分发；衍生作品同样开源。
4. 不引入 GPL 不兼容的依赖（例如把闭源 SDK 静态链入）。

## 依赖许可证政策

| 依赖 | 许可证 | 结论 |
|---|---|---|
| .NET 10 runtime / SDK | MIT | ✅ 兼容 |
| Avalonia 12.1.6 | MIT | ✅ 兼容 |
| xunit / Microsoft.NET.Test.Sdk | Apache-2.0 / MIT | ✅ 兼容（Apache-2.0 与 GPLv3 兼容） |
| Velopack（M10 候选） | MIT | ✅ 兼容 |
| authlib-injector（M3c 候选） | GPL-3.0 | ✅ 同协议，可随包分发 |
| Mojang 官方库 / 资源 | Mojang 各自条款 | ⚠️ 运行期下载，不随仓库分发；仅在实例目录缓存 |
| CurseForge API | 其 API ToS | ⚠️ M8 前必须确认 key 与再分发限制 |

新增依赖前：查许可证 → 写进本表 → 再 `dotnet add package`。

## 明确不做

- 不把 Mojang 的游戏文件、assets、libraries 提交进仓库（运行期按官方 manifest 下载并校验）。
- 不把任何用户令牌、账号、日志提交进仓库。
- 不宣称"参考了思路"来掩盖"抄了代码"——两者在 GPL 下的义务不同，登记时必须写清楚。
