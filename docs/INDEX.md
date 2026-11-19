# docs/INDEX · 事实源索引

> 读文档的顺序就是思考的顺序；每份文件只回答一个问题，重复即删除。

| 文件 | 回答什么问题 | 何时更新 |
|---|---|---|
| `../PROGRESS.md` | 现在在哪、上轮做完什么、下一步是什么 | 每轮结束 |
| `../ROADMAP.md` | 里程碑是什么、判据是什么 | 里程碑边界变化时 |
| `../AGENTS.md` | 未来会话必须遵守什么 | 红线变化时 |
| `env-report.md` | 机器与网络到底什么样（实测） | 每次环境探测后追加小节 |
| `DECISIONS.md` | 哪些小事是我替你定的、怎么推翻 | 新增默认值时 |
| `adr/0001-ui-framework.md` | 为什么是 Avalonia 12 | 推翻选型时 |
| `adr/0002-license-gpl3.md` | 为什么 GPL-3.0、能抄什么不能抄什么 | 许可证/依赖策略变化时 |
| `M1-metadata-download.md` | M1 的实测结构、规模数字、裁剪决定与下载验收记录 | M1 期间持续更新 |
| `M2-launch-core.md` | 启动核心的组成、关键约定、命令行顺序与验收记录 | M2 期间持续更新 |

## 待建（按里程碑顺序）

- M1：`M1-metadata-download.md`（manifest/version.json/assets/natives 的字段与坑）
- M2：`M2-launch-core.md`（arguments 双格式、规则求值、classpath）
- M4：`M4-java-runtime.md`（JDK 矩阵、Mojang 运行时源）
