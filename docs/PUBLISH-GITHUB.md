# 把项目推到 GitHub（公开仓库 · 一次做完）

> 目标：`https://github.com/<owner>/es-luncher` 公开可访问，CI 绿、首个 Release 可下载、自动更新能指向该 Release。
> 本文用 `<owner>` 占位，替换成你的 GitHub 用户名（或组织名）。
> CHANGELOG.md 末尾的对比链接里也有 <owner>，一并替换。

## 0. 前置检查（本地，已确认）

| 项 | 状态 |
|---|---|
| 仓库 | `D:\dev\es-luncher`，分支 `main`，81 个提交 |
| 工作区 | 干净 |
| 许可证 | GPL-3.0-or-later（`LICENSE`） |
| 仓库体积 | 213 个跟踪文件 / `.git` 3.3 MB（无二进制垃圾、无凭据） |
| 远端 | **尚未配置** |

> 提交身份目前是占位 `HP <hp@mc-launcher.local>`。按决定**不改写历史**，只把以后的提交改成真实身份（见第 2 步）。

## 1. 在网页上建空仓库

1. 打开 <https://github.com/new>
2. **Repository name**：`es-luncher`
3. **Visibility**：Public
4. **不要**勾选 "Add a README file" / ".gitignore" / "license"（本地已有一套，勾了会冲突）
5. Create repository

## 2. 接上远端并推送

```powershell
cd D:\dev\es-luncher

# 只加远端（先不推，等仓库建好）
powershell -NoProfile -File scripts\init-github.ps1 -Owner <owner> `
    -Name "你的名字" -Email "you@example.com"

# 或者一步到位：加远端并立即推送（首次会弹浏览器登录 Git Credential Manager）
powershell -NoProfile -File scripts\init-github.ps1 -Owner <owner> `
    -Name "你的名字" -Email "you@example.com" -Push
```

手动等价命令：

```powershell
git config --local user.name  "你的名字"
git config --local user.email "you@example.com"
git remote add origin https://github.com/<owner>/es-luncher.git
git push -u origin main
```

## 3. 填仓库信息（About 区）

**Description**（直接复制）：

```
轻量、开源、无广告的 Minecraft Java 版启动器：全版本下载、实例管理、Fabric/Quilt/Forge 加载器、Modrinth 内容管理、崩溃诊断与增量自动更新。
```

**Topics**（逐个粘）：

```
minecraft  minecraft-launcher  launcher  avalonia  dotnet  csharp
fabric  quilt  forge  modrinth  desktop-app  windows
```

勾选 **Releases / Packages**（About 右侧），把 `docs/CODE-SIGNING.md` 之类不需要露出。

## 4. 打开仓库的几项设置

| 位置 | 建议 |
|---|---|
| Settings → Actions → General | Allow all actions（CI 与 release 要跑） |
| Settings → Code security | 打开 **Dependabot alerts** 与 **Secret scanning**（免费） |
| Settings → General → Features | Issues 开、Discussions 可选开 |
| Settings → Pages | 不需要（文档放仓库里就够） |

推送后 CI 会自动跑（`.github/workflows/ci.yml`）。绿了以后把徽章加进 `README.md` 顶部：

```markdown
[![ci](https://github.com/<owner>/es-luncher/actions/workflows/ci.yml/badge.svg)](https://github.com/<owner>/es-luncher/actions/workflows/ci.yml)
```

## 5. 首个 Release（0.1.5）

本地已经打好包：`D:\dev\es-luncher\artifacts\releases\`。发布步骤：

```powershell
cd D:\dev\es-luncher
git tag -a v0.1.5 -m "ES Luncher 0.1.5"
git push origin v0.1.5
```

然后在 GitHub → Releases → **Draft a new release**：

- Tag：`v0.1.5`（选刚推的）
- Title：`ES Luncher 0.1.5`
- 说明：把 `CHANGELOG.md` 的 0.1.5 段落贴进去
- 上传这些资产（从 `artifacts\releases\`）：
  - `ESLuncher-win-Setup.exe`
  - `ESLuncher-win-Portable.zip`
  - `ESLuncher-0.1.5-full.nupkg`
  - `ESLuncher-0.1.5-delta.nupkg`（如果上一个版本也发过）
  - `releases.win.json`、`assets.win.json`、`RELEASES`
  - `SHA256SUMS-0.1.5.txt`
- 勾选 **Set as the latest release** → Publish

> 也可以走 `release.yml` 工作流：Actions → release → Run workflow → 填 `0.1.5`（它会构建、测试、出 zip、建 Draft Release；签名留空表示不签）。两条路选一条，别同时发。

## 6. 自动更新指向 GitHub

代码已支持任意更新源：

```powershell
# 用环境变量指到 Release 下载页
setx ESLUNCHER_UPDATE_SOURCE "https://github.com/<owner>/es-luncher/releases/latest/download"
```

Velopack 的 GitHub 源要求 Release 资产里带 `releases.win.json` + `*.nupkg`（第 5 步已上传）。
若之后要写死进代码，把 `AppServices.CreateUpdateService()` 的 `source` 默认值改成该 URL，并在 `--source`/设置里保留手动覆盖。

## 7. 签名（拿到证书后）

仓库公开后可以申请 **SignPath Foundation**（免费，要求 OSI 许可 + 公开仓库，GPL-3.0 符合）：

1. <https://signpath.org> 申请 Foundation → 审核通过
2. 在 SignPath 建 Project `es-luncher`、Artifact Configuration `windows-zip`、Signing Policy `release-signing`
3. 仓库 Secrets 加 `SIGNPATH_API_TOKEN`、`SIGNPATH_ORGANIZATION_ID`；Variables 加 `SIGNPATH_ENABLED=true` 等（见 `docs/SIGNPATH-SETUP.md`）
4. 重新跑 release 工作流，签名自动完成

在手头没有证书之前，发版时如实写「未签名，SmartScreen 可能提示」，并给出 `SHA256SUMS`。

## 8. 公开前最后确认（可选取舍）

| 文件 | 是否公开 | 说明 |
|---|---|---|
| `PROGRESS.md` | 建议保留 | 是项目的证据链，也是卖点；但含内部工作记录与个人决策 |
| `AGENTS.md` | 建议保留 | 给贡献者看的红线，价值高 |
| `docs/DECISIONS.md` | 建议保留 | 默认值与其推翻方式 |
| `src/EsLuncher.App/Assets/credits.json` | 保留 | 鸣谢名单（已有「项目发起 / AI 编程助手」两行） |
| `assets/icon/endless-studio-logo.jpg` | 保留 | 工作室图标，只在关于页使用 |

如果不想公开内部记录，把 `PROGRESS.md` 与 `AGENTS.md` 移出跟踪（`git rm --cached`）即可，仓库功能不受影响。

## 9. 推完自查

```powershell
git ls-files | Measure-Object                 # 应约 220 个文件
git log --oneline -1                          # 与远端一致
git remote -v                                 # origin 指向你的仓库
```

远端侧：

- Actions 里 `ci` 是绿的；
- Releases 里有 `v0.1.5` 且 7 个资产齐全；
- 随便点开一个源文件，中文不乱码；
- About 区描述与 topics 已填。
