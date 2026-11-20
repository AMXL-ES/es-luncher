# M5 · 实例系统

> 目标：同一份共享库/资源下，多实例各自独立配置、可并行运行、可克隆/导入/导出。
> 结果：实例系统跑通，**两个 1.18.2 实例同时运行并各自 exit 0**。

## 1. 目录与配置

```
D:\mc-data\
  libraries\ assets\ versions\ java\        ← 共享（实例之间去重）
  instances\<实例名>\
    config.json                             ← 实例配置（JSON）
    game\                                   ← --gameDir（世界/options/mods）
    natives\                                ← 每次启动前按需补齐
```

- 实例名允许**中文与空格**（实测 `测试 实例`），禁止路径分隔符与 Windows 非法字符、禁止首尾空格。
- 共享 `libraries/assets`，实例只存配置 + 游戏目录 + natives。

## 2. 配置模型（`InstanceConfig`，JSON）

| 字段 | 说明 |
|---|---|
| `name` / `versionId` | 实例名与目标版本 |
| `memoryMb` | `-Xmx`（默认 2048） |
| `javaPath` | 显式指定 java.exe（空 = 按 version.json 自动选，托管运行时优先） |
| `username` / `accountKey` | 离线用户名 / 已保存账号（DPAPI 存储键） |
| `extraJvmArguments` / `extraGameArguments` | 额外参数 |
| `windowWidth` / `windowHeight` | 启动时追加 `--width/--height` |
| `createdAt` | 创建时间 |

写入是原子的（`.tmp` + 替换）。

## 3. CLI

```powershell
esluncher instance create "整合包 1.18" --version 1.18.2 --memory-mb 4096
esluncher instance list
esluncher instance clone "整合包 1.18" "试验 副本"
esluncher instance export "整合包 1.18" --out D:\backup\pack.zip
esluncher instance import D:\backup\pack.zip "还原 实例"
esluncher instance delete "试验 副本" --force
esluncher launch --instance "整合包 1.18" --autostop 30
```

- 删除必须 `--force`（会连同 `game/` 一起删）。
- 导入会做 zip-slip 校验，失败时清理半成品目录。
- 启动实例时自动补齐该实例的 natives（共享库 jar 已存在，只解压）。

## 4. 验收记录（2026-11-20）

| 项 | 结果 |
|---|---|
| 测试 | **121 passed / 0 failed**（新增 13 个实例测试：创建/重名/非法名/保存/克隆/删除/导出导入/zip-slip/缺配置） |
| 命令实测 | create ×2、list、clone、export、import 全部成功；`测试 实例`/`克隆 实例`/`导入 实例`/`dev-two` 共 4 个 |
| 配置生效 | `launch --instance "测试 实例"` → `-Xmx1536m`、`working: instances\测试 实例\game`、natives 59 文件补齐 |
| 单实例启动 | 30 s autostop，主菜单 atlas 全建，exit 0 |
| **并行双实例** | `测试 实例`(1536 MB) 与 `dev-two`(2048 MB) 同时启动 → **两个 "Minecraft 1.18.2" 窗口并存**，各自日志到主菜单，随后关窗**两个都 exit 0**（各 64.4 s） |
| 日志隔离 | `launch-测试 实例-1.18.2-<time>-<rand>.log` 与 `launch-dev-two-…` 互不覆盖 |

**修掉的坑**：最初日志文件名只含版本+秒级时间戳，两个实例同秒启动时**文件锁冲突**导致第二个实例启动失败。现改为「实例名 + 版本 + 时间 + 4 位随机」，实测并行不再冲突。

## 5. 已知缺口

1. **导入范围**：目前导入的是本启动器导出的 zip；导入其它启动器（`.minecraft`、Prism 实例）的目录扫描留到 M8 前后。
2. 实例级资源包/shader 管理未做（属 M7 UI 与 M8）。
3. 没有实例配额/磁盘统计；`game` 目录很大时导出会很慢（未做流式压缩）。
4. 账号自动续期仍未接入启动流程（M3b 遗留）。
