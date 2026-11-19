# M3 · 认证（离线 / 微软 / 皮肤站）

> 用户裁剪：**先只做离线，给微软与皮肤站留位置**。
> 结果：离线完成并已在 M2 真实使用；微软**代码完整**但未做真实登录（缺你提供的 Azure client ID）；皮肤站按裁剪**暂缓**。

## 1. 组成

| 能力 | 落点 | 状态 |
|---|---|---|
| 认证抽象 + 离线身份 | `Auth/AuthModels.cs`、`Launching/OfflineIdentity.cs` | ✅ 完成，M2 已用 |
| 微软设备码全链路 | `Auth/MicrosoftAuthClient.cs` | ✅ 代码完成，单元测试覆盖；⏸ 真实登录待 client ID |
| 凭据加密落盘（DPAPI） | `Auth/Dpapi.cs`、`Auth/TokenStore.cs` | ✅ 完成，实测密文落盘 |
| CLI | `glauncher login/accounts/logout`、`launch --account` | ✅ 完成 |
| 皮肤站外置登录 | —— | ⏸ 按裁剪暂缓（留 `IAuthProvider` 扩展点） |

## 2. 微软链路（全部官方公开接口，不伪造任何身份）

```
设备码 POST login.microsoftonline.com/consumers/oauth2/v2.0/devicecode
  ↓ 轮询 token 端点（authorization_pending / slow_down / expired_token / authorization_declined）
MSA access_token
  ↓ POST user.auth.xboxlive.com/user/authenticate   (RpsTicket = "d=" + msa)
XBL token
  ↓ POST xsts.auth.xboxlive.com/xsts/authorize      (RelyingParty = rp://api.minecraftservices.com/)
XSTS token + uhs
  ↓ POST api.minecraftservices.com/authentication/login_with_xbox   (identityToken = "XBL3.0 x=<uhs>;<xsts>")
Minecraft access_token（≈24h）
  ↓ GET  api.minecraftservices.com/minecraft/profile (Bearer)
profile id + name
```

- 轮询间隔遵循服务器给的 `interval`；收到 `slow_down` 时 **+5 s**（单测覆盖：1s → 1s → 6s）。
- `XErr` 映射成人话：2148916233 → 先创建 Xbox 档案；2148916235 → 地区限制；2148916236/7 → 成人验证；2148916238 → 儿童账号需家庭组。
- 401（token 失效）单独报"需要重新登录"，不当成网络错误。

## 3. 凭据安全

- **DPAPI**（`CryptProtectData`，`CRYPTPROTECT_UI_FORBIDDEN`）直接 P/Invoke，不引入额外 NuGet 包。
- 落盘位置：`D:\mc-data\auth\accounts\<账号名>.bin`；文件是 DPAPI blob（实测头 8 字节 `01 00 00 00 d0 8c 9d df`）。
- **实测文件里搜不到明文**（`"Dev"` 与 token 均不可见）；`AuthSession.Describe()` 只输出名字/UUID/类型/过期时间，**永不输出 token**（单测断言）。
- 非 Windows 平台明确抛 `PlatformNotSupportedException`，不降级成明文存储。

## 4. CLI

```powershell
glauncher login --provider offline --username Dev        # 保存离线账号（DPAPI）
glauncher accounts                                       # 列出账号（不含 token）
glauncher login --provider microsoft --client-id <id>    # 设备码登录
glauncher launch --version 1.18.2 --account dev --autostop 30
glauncher logout --account dev
```

- `launch --account` 会加载会话并把 profile id / token / userType 填进启动参数；离线会话仍是 `userType=legacy`、token `0`。
- 微软客户端 ID 也可用环境变量 `GLAUNCHER_MSA_CLIENT_ID` 提供；缺失时 CLI 直接给出注册步骤并 exit 2，**不会拿别人的 client ID 顶替**。

## 5. 验收记录（2026-11-19）

| 项 | 结果 |
|---|---|
| 测试 | `dotnet test` → **91 passed / 0 failed**（新增 12 个认证测试） |
| 离线登录 | `login --provider offline --username Dev` → exit 0，`dev.bin` 390 字节 |
| 账号列表 | `accounts` → `dev  Dev (380df991…) via offline, userType=legacy`（无 token） |
| 加密检查 | DPAPI blob 头 `01 00 00 00 d0 8c 9d df`；UTF-8 全文不含 `Dev` |
| 微软缺 client id | exit 2 + 三步注册指引 |
| 微软全链路（假 HTTP） | 设备码 → pending → token → XBL → XSTS → MC login → profile，最终 `mc-token` / `uhs-123` / `userType=msa` |
| 错误路径 | XSTS 2148916233 → "先创建 Xbox 档案"；设备码过期 → 明确报错；401 → "需要重新登录" |

## 6. 待办 / 未验证

1. **真实设备码登录**：需要你在 Azure 注册一个公共客户端应用（个人账号免费，重定向 `http://localhost`），把 client ID 给我或写进环境变量。
2. **token 自动续期接入启动流程**：目前 `launch` 只对过期会话给警告；续期 API 已实现（`RefreshAsync`），一键接线留到 M3b 收尾。
3. **皮肤站**：`authlib-injector` 注入 `-javaagent` + 外置登录 API；按裁剪暂缓，`IAuthProvider` 已留扩展点。
4. 非 Windows 的 keyring 实现（M10 跨平台时再说）。
