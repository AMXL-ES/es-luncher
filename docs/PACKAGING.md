# 打包说明（明天用）

## 前置
- .NET SDK 10.0.401（已装）
- Inno Setup 6（官网 jrsoftware.org/isdl.php，装好后把 `ISCC.exe` 加入 PATH 或记路径）

## 1. 构建便携包

```powershell
cd D:\dev\es-luncher
# framework-dependent（36 MB，需要 .NET 10 Desktop Runtime）
powershell -NoProfile -File scripts\publish-portable.ps1 -Version 0.1.0

# 或 self-contained（约 150 MB，装完直接跑，推荐给安装包）
powershell -NoProfile -File scripts\publish-portable.ps1 -Version 0.1.0 -SelfContained
```

产物：`artifacts\EsLuncher-0.1.0-win-x64\` 与 `artifacts\EsLuncher-0.1.0-win-x64.zip`

## 2. 生成安装包

```powershell
# 用 Inno Setup 6 的 ISCC 编译（默认读 installer\es-luncher.iss）
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\es-luncher.iss

# 换版本号：
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=0.1.0 installer\es-luncher.iss
```

产物：`artifacts\ESLuncher-0.1.0-setup.exe`

> 安装包会打包 `artifacts\EsLuncher-0.1.0-win-x64\`；如果上一步用了 `-SelfContained`，安装后不依赖 .NET 运行时。

## 3. 签名（可选，需要证书）

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f cert.pfx /p <密码> `
  artifacts\ESLuncher-0.1.0-setup.exe
```

无证书时 SmartScreen 会提示“未知发布者”，这是预期。

## 4. 发布前最后检查

```powershell
dotnet build EsLuncher.slnx -c Release
dotnet test EsLuncher.slnx
powershell -NoProfile -File scripts\smoke-ui.ps1
```

- 构建 0 警告、202 个测试全绿、UI 冒烟通过
- 便携 exe 双击能起、能启动一个实例
- 安装包安装/卸载各试一次
