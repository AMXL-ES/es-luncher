# GitHub Actions + SignPath 接入（开源免费签名）

> 目标：push tag `v0.1.0` → 自动构建、测试、打包、签名、建 GitHub Release。

## 1. 申请 SignPath Foundation（免费，开源项目）
1. 打开 https://signpath.org → 「Apply for Foundation」
2. 要求：公开仓库、OSI 许可（我们是 GPL-3.0）、项目真实可构建
3. 填写仓库地址、许可、用途，提交
4. 审核通过后登录 SignPath，创建：
   - **Project**：`es-luncher`
   - **Artifact Configuration**：`windows-zip`
     - 输入：zip（里面是 `EsLuncher.exe` 等）
     - 规则：签名 `EsLuncher.exe`
   - **Signing Policy**：`release-signing`
     - 建议勾选「需要手动批准」：每次 Release 前你点一次批准
5. 记录 **Organization ID**（组织设置页）和 **API Token**（用户设置 → API tokens）

## 2. 配置 GitHub 仓库
Settings → Secrets and variables → Actions：

### Secrets
| 名称 | 值 |
|---|---|
| `SIGNPATH_API_TOKEN` | SignPath 生成的 token |
| `SIGNPATH_ORGANIZATION_ID` | SignPath 组织 ID |

### Variables
| 名称 | 值 |
|---|---|
| `SIGNPATH_ENABLED` | `true`（不签就留空/false） |
| `SIGNPATH_PROJECT_SLUG` | `es-luncher` |
| `SIGNPATH_POLICY_SLUG` | `release-signing` |
| `SIGNPATH_ARTIFACT_CONFIG` | `windows-zip` |

## 3. 触发发布
```powershell
git tag v0.1.0
git push origin v0.1.0
```
或 Actions 页面手动 Run workflow，填版本号。

流程：
1. `dotnet build` / `dotnet test`（202 个测试）
2. `scripts/publish-portable.ps1` 出 zip
3. 上传未签名 zip → SignPath 签名 → 下载已签名 zip
4. 解包 → Inno Setup 出安装包
5. 生成 SHA256SUMS → 建 Draft Release

> SignPath 未启用（`SIGNPATH_ENABLED=false`）时，第 3 步跳过，直接出**未签名**安装包，Release 说明里保留 SmartScreen 提示。

## 4. 本地等价命令（明天）
```powershell
powershell -NoProfile -File scripts\publish-portable.ps1 -Version 0.1.0
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\es-luncher.iss
```

## 5. 常见问题
- **SignPath 返回 403**：policy 需要手动批准，去 SignPath 控制台点 Approve
- **安装包没签名**：artifact configuration 必须包含 `EsLuncher.exe`，且 policy 作用于 zip 内文件
- **Release 是 draft**：去 GitHub Releases 手动点 Publish
- **想同时签 CLI**：加第二个 artifact configuration，或把 CLI 也打进 zip
