# 需要申请 / 拿到的 API 与凭据（简版）

> 用途：ES Luncher 里少数功能需要外部凭据。按优先级列，**目前只有微软登录需要你动手申请**。

## 1. 微软登录（Azure 公共客户端 ID）— 必需，若要用正版登录

| 项 | 值 |
|---|---|
| 你要拿到 | **Application (client) ID**（形如 `xxxxxxxx-xxxx-...`） |
| 放哪 | 环境变量 `ESLUNCHER_MSA_CLIENT_ID`（或启动器里未接的 `--client-id`） |
| 费用 | 免费（个人 Azure 账号即可） |

步骤：
1. 打开 https://portal.azure.com → 登录（个人微软账号即可）
2. 「Microsoft Entra ID」→「应用注册」→「新注册」
3. 名称随意（如 `ES Luncher`）；**受支持的账户类型**选「**仅个人 Microsoft 账户**」（代码走 consumers 端点）
4. 重定向 URI：平台选「公共客户端/本机」，填 `http://localhost`
5. 注册后复制「应用程序(客户端) ID」
6. 进「身份验证」→ 打开「**允许公共客户端流**」→ 保存
7. 设置环境变量并重启启动器：
   ```powershell
   setx ESLUNCHER_MSA_CLIENT_ID "你的-client-id"
   ```
8. 启动器 → 账户 → 「微软登录（设备代码）」

> 代码用的 scope：`XboxLive.signin`、`XboxLive.offline_access`；设备代码端点 `login.microsoftonline.com/consumers`。

## 2. CurseForge API Key — 可选（当前未接）

| 项 | 值 |
|---|---|
| 你要拿到 | **CurseForge API Key** |
| 放哪 | 预留 `ESLUNCHER_CF_API_KEY`（还没实现读取） |
| 费用 | 免费，需同意 API 条款 |

步骤：
1. https://console.curseforge.com → 登录 → 「API Keys」→「Create API Key」
2. 填应用名/用途，同意条款，复制 key
3. 注意：CurseForge 条款限制第三方启动器自动下载，需按官方 API 规范使用

## 3. Modrinth — 不需要 key

- 公开 API，直接可用；建议请求带 `User-Agent: ESLuncher/0.1 (contact)`
- 目前代码没有设置 UA，后续可补（不影响功能）

## 4. Mojang / 微软账号资源 API — 不需要 key

- 版本清单、资源、库文件都是公开下载
- 不要用已废弃的 Yggdrasil / authserver 直连登录

## 5. 可选

| 用途 | 需要 |
|---|---|
| GitHub Release 自动发布 | Personal Access Token（`repo` 权限） |
| 反馈/公告推送 | Discord Webhook URL |
| 国内镜像测速 | 无需 key（BMCLAPI 等） |

## 汇总表

| 凭据 | 状态 | 影响功能 |
|---|---|---|
| 微软 client ID | **等你申请** | 微软正版登录 |
| CurseForge key | 未接 | CurseForge 下载（暂时没有） |
| Modrinth | 不需要 | 模组/资源包/光影/数据包/整合包 |
| Mojang | 不需要 | 下载/启动 |
