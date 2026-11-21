# 代码签名证书：怎么搞（2026 现状）

> 关键前提：**2023 年 6 月起，公开代码签名证书的私钥必须存放在硬件里**（USB Token / HSM / 云 HSM）。
> 所以 CA 不会再给你一个能直接拷走的 `.pfx`；网上那种"买个 pfx 文件"的教程已经过时。

## 选哪种

| 方案 | 价格（约） | 适合 | SmartScreen |
|---|---|---|---|
| **Azure Trusted Signing** | ~$10/月（Azure 订阅） | 个人/小团队，微软自家服务 | 需信誉积累，但签名链受信 |
| **OV 代码签名证书**（DigiCert / Sectigo / SSL.com / Certum / GlobalSign） | $200–400/年 + 硬件 Token | 个人或公司，传统方式 | 需下载量积累信誉 |
| **EV 代码签名证书** | $300–600/年 + Token/HSM | 想**立刻**减少 SmartScreen 警告 | 立刻受信（EV 信誉） |
| **SignPath Foundation** | 免费 | **开源项目**（GPL 可以申请） | 受信，CI 集成 |
| **Certum Open Source Code Signing** | ~€100/年（Token 另计） | 开源项目 | 受信 |
| 自签名证书 | 免费 | 仅内部测试 | **仍然警告**，不要发给用户 |

## 推荐顺序（这个项目）
1. **SignPath Foundation**（免费，开源 GPL 正好）→ 申请通过后 CI 自动签
2. **Azure Trusted Signing**（约 $10/月，个人可申请，需身份验证）
3. **OV 证书**（SSL.com / Certum 较便宜，需 Token）
4. EV（预算充足、想立刻消警告）

## OV/EV 申请流程（传统 CA）
1. 选 CA 下单（个人用身份证/护照；公司用营业执照）
2. 提交身份/组织验证：电话回拨、地址、域名邮箱等
3. CA 寄 **USB Token**（或让你在云端 HSM 生成密钥）
4. 在 Token 上生成密钥 + 提交 CSR → CA 签发证书进 Token
5. 装 Token 驱动，用 `signtool` 签名：
   ```powershell
   signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a `
     /n "Endless Studio" artifacts\ESLuncher-0.1.0-setup.exe
   ```
6. 验证：
   ```powershell
   signtool verify /pa /v artifacts\ESLuncher-0.1.0-setup.exe
   Get-AuthenticodeSignature artifacts\ESLuncher-0.1.0-setup.exe
   ```
7. 每年续费 + 重新验证

## Azure Trusted Signing 流程（较便宜）
1. 有 Azure 订阅 → 搜索「Trusted Signing」→ 创建账户
2. 做 **Identity Validation**（个人或组织，提交证件，1–3 周）
3. 创建 Certificate Profile（Public Trust）
4. 本机安装 signtool + 下载 `Azure.CodeSigning.Dlib.dll`
5. 写 `metadata.json`：
   ```json
   {
     "Endpoint": "https://<region>.codesigning.azure.net",
     "CodeSigningAccountName": "<account>",
     "CertificateProfileName": "<profile>"
   }
   ```
6. 签名：
   ```powershell
   signtool sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 `
     /dlib "C:\tools\Azure.CodeSigning.Dlib.dll" /dmdf metadata.json `
     artifacts\ESLuncher-0.1.0-setup.exe
   ```

## 开源免费：SignPath Foundation
1. https://signpath.org → 申请 Foundation（要求项目 OSI 许可、公开仓库）
2. 通过后把签名步骤接进 GitHub Actions（他们给模板）
3. 产物由 SignPath 云端 HSM 签，私钥不出他们的环境

## 暂时不签也能发
- 发布页同时给 **SHA256SUMS.txt**（`scripts/publish-portable.ps1` 已生成）
- 说明"未签名，SmartScreen 可能提示未知发布者"
- 用户可校验哈希后再运行

## 已接通的签名链路（2026-11-21 实测）

`scripts\pack-velopack.ps1` 支持三条路，选一个参数即可：

| 方式 | 参数 | 依赖 |
|---|---|---|
| **PFX 直接签**（无需 signtool / Windows SDK） | `-PfxPath <pfx> -PfxPassword <pwd>` | 证书 pfx；实际由 `scripts\sign-file.ps1` 用 `Set-AuthenticodeSignature`（SHA256 + DigiCert 时间戳）逐文件签 |
| signtool | `-SignParams "/fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /n \"Endless Studio\""` | 装 Windows SDK 的 `signtool.exe`，证书在证书存储里 |
| Azure Trusted Signing | `-AzureTrustedSignFile <metadata.json>` | Azure 订阅 + 身份验证 |

### 自签测试结果（本机，只为验证流程）

```
证书    CN=Endless Studio (ES Luncher test), O=Endless Studio
指纹    8AD7985DD218DA6D50B2B4C06F0759D70931033E（有效至 2028-11-21）
命令    powershell -NoProfile -File scripts\pack-velopack.ps1 -Version 0.1.4 `
            -PfxPath D:\dev\test-signing\es-luncher-test.pfx -PfxPassword es-luncher-test
结果    Setup.exe           SignatureType=Authenticode
                            Signer=CN=Endless Studio (ES Luncher test)
                            Timestamp=CN=DigiCert SHA256 RSA4096 Timestamp Responder 2026 1
                            Status=UnknownError（"证书链终止于不受信任的根证书"）
        nupkg 内 EsLuncher.exe / Portable 的 current\EsLuncher.exe 同样带签名
```

**这条链路只证明「流程可用」，不解决 SmartScreen**：自签名根不受信，用户侧仍然提示未知发布者。
要让 Windows 和用户信任，必须拿到真实证书（上表的 OV / EV / Azure / SignPath 任选），
然后把 **pfx 文件 + 密码**（或 signtool 证书指纹、Azure metadata.json）交给我，改一个参数即可。

### 清理自签材料

```powershell
Remove-Item D:\dev\test-signing -Recurse -Force
Remove-Item Cert:\CurrentUser\My\8AD7985DD218DA6D50B2B4C06F0759D70931033E
```

## 时间线
- 传统 OV：3–7 个工作日（加急 1–2 天）
- EV：类似，Token 邮寄另算
- Azure Trusted Signing：身份验证 1–3 周
- SignPath Foundation：1–2 周审核
