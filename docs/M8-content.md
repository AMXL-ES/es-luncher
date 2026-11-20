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
| 测试 | **148 passed / 0 failed**（内容 10 个 + 模组元数据 4 个 + 其余） |
| 实测 | `content add` → 240 B jar 落 `instances\Fabric 测试\game\mods`；`list` 显示；`remove` 后列表为空，文件在 `trash\content\Fabric 测试\<时间戳>-example-mod.jar` |
| 安全 | 路径穿越（`../escape.jar`、`a/b.jar`）被拒；未知实例被拒 |
| UI | 实例页底部「内容」面板：类型切换 / 添加（文件选择器）/ 移除 / 打开目录；mods 自动读元数据（`Example Mod · 1.2.3 · fabric · 240 B`） |
| 元数据 | `fabric.mod.json` → id/name/version/depends；`META-INF/mods.toml` → modId/version/displayName；坏 jar 不抛异常 |

## 4. 模组元数据（`Content/ModMetadata.cs`）

- Fabric：解析 `fabric.mod.json`，依赖取 `depends` 里除 `minecraft/fabricloader/java/fabric-api` 之外的条目。
- Forge/NeoForge：轻量解析 `META-INF/mods.toml`（`[[mods]]` 块 + `[[dependencies.*]]` 的 modId），不引第三方 TOML 库。
- 解析失败/非模组 jar → `unknown`，UI 显示 `—`，绝不因坏 jar 崩溃。

## 6. Modrinth + .mrpack（2026-11-20 本轮）

| 能力 | 落点 | 证据 |
|---|---|---|
| Modrinth 检索 | `Content/ModrinthClient.cs` | `modrinth search --query jei --version 1.18.2 --loader fabric` → JEI 86,313,860 下载等 5 条 |
| Modrinth 安装 | 同上 | `modrinth install --instance "Fabric 测试" jei` → `jei-1.18.2-fabric-10.2.1.1002.jar`（1.1 MB） |
| 依赖实况 | —— | 先只装 JEI → 启动失败（`HARD_DEP fabric`）；装 `fabric-api` 后启动成功：**`Loading 51 mods`**、JEI atlas 创建 |
| `.mrpack` 导出/导入 | `Content/InstancePackService.cs` | 导出 971,964 B；导入到同版本实例：下载 0 / 覆盖 1 / 跳过 0 |

- 导出用 `overrides/mods/*`（不编造下载 URL）；导入支持 `files[].downloads` + `overrides/*`。
- 加载器名映射：实例 id 的 `fabric-loader` → Modrinth 的 `fabric`；`quilt-loader` → `quilt`。
- 路径穿越防护：包内 `../` 直接拒绝（单测 + 真实）。

## 7. 待办（M8 剩余）

1. jar 内依赖版本范围与冲突提示（当前只列 id）。
2. Modrinth 检索/下载、`.mrpack` 导入导出、CurseForge（需 API key）。
3. 资源包/光影包的启用/禁用（`disabled` 后缀或移动目录）。
4. 内容列表排序/搜索与批量操作。
