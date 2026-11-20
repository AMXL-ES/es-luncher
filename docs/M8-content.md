# M8 · 内容管理（进行中）

> 目标：mods / 资源包 / 光影包的安装、列出、移除；不破坏用户文件。
> 本轮：Core 服务 + CLI；UI 面板留给下一刀。

## 1. Core（`Content/InstanceContent.cs`）

| 能力 | 说明 |
|---|---|
| `ContentKind` | `Mods` / `ResourcePacks` / `ShaderPacks`，目录 `mods` / `resourcepacks` / `shaderpacks` |
| `List` | 返回文件名、大小、修改时间（不读 jar 元数据，先保持轻量） |
| `Add` | 复制进实例目录（同名覆盖），源文件不存在直接报错 |
| `Remove` | **移入 `数据根/trash/content/<实例>/<时间戳>-<文件名>`**，不真删；可手动找回 |

**为后续功能留的口子（别做死）**：

1. 加新内容类型：`ContentKind` 加枚举 + `FolderName` 映射即可，不需要改调用方。
2. `List` 返回 `ContentFile` 记录，将来可加 `ModId/Version/Dependencies` 字段而不破坏现有调用。
3. 删除是"复制到回收站 + 列表隐藏"，将来要做"还原/清空回收站"只需读 trash 目录。
4. 依赖解析/冲突检测将来放独立服务，读同一批文件，不动 `InstanceContentService` 语义。

## 2. CLI

```powershell
esluncher content list   --instance "Fabric 测试" --kind mods
esluncher content add    --instance "Fabric 测试" --kind mods D:\downloads\jei.jar
esluncher content remove --instance "Fabric 测试" --kind mods jei.jar
```

## 3. 验收记录（2026-11-20）

| 项 | 结果 |
|---|---|
| 测试 | **144 passed / 0 failed**（新增 10 个内容测试：空目录/添加/移除/回收站/非法名/缺源文件/未知实例/类型解析） |
| 实测 | `content add` → 21 B 文件落 `instances\Fabric 测试\game\mods`；`list` 显示 1 个；`remove` 后列表为空，文件在 `trash\content\Fabric 测试\20261120-192950-909-demo-mod.jar` |
| 安全 | 路径穿越（`../escape.jar`、`a/b.jar`）被拒；未知实例被拒 |

## 4. 待办

1. UI：实例页加"内容"抽屉（list/add/remove + 打开 mods 目录）。
2. jar 内 `fabric.mod.json` / `mods.toml` 解析（mod 名称、版本、依赖）→ 冲突提示。
3. Modrinth/CurseForge 下载集成（M8 后续，先做本地文件）。
4. 资源包/光影包的目录约定确认（shaderpacks 对 Iris/OptiFine 都可用）。
