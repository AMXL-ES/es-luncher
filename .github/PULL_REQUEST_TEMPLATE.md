## 这个 PR 做了什么

<!-- 一句话说明。关联 issue 用 "Closes #123"。 -->

## 证据（无证据不算完成）

```powershell
# 贴命令与输出摘要，例如：
dotnet build EsLuncher.slnx -c Release   # 0 警告 0 错误
dotnet test  EsLuncher.slnx -c Release   # 204 passed
```

## 自查

- [ ] 业务逻辑在 `EsLuncher.Core`，UI 只做编排（未违反架构守卫测试）
- [ ] 新增/修改的关键路径有单测；修 bug 时先写了会失败的回归测试
- [ ] 构建 0 警告（`TreatWarningsAsErrors` 已开）
- [ ] 没把凭据、日志、用户路径写进代码或提交
- [ ] 涉及不可逆决定时已更新 `docs/adr/` 与 `docs/DECISIONS.md`
- [ ] 如移植了 GPL-3.0 代码，已在文件头注明来源并更新 `NOTICE`
