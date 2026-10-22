# Sydney Buddy · 悉尼旅行 Agent

**React + TypeScript 网页前端、原生 Android（Kotlin + Jetpack Compose）与 ASP.NET Core（C# / .NET 10）后端，通过 EF Core 和 PostgreSQL 保存行程与偏好。** 单题引导收集旅行偏好，NVIDIA Nemotron 推荐候选地点，程序独立检查路线约束，再由用户确认保存。地图、地点、路线与天气使用 Google Maps Platform，授权偏好检索使用官方 Meterless H-MEM 引擎。

本次迁移将 Python API 替换为 C#，并增加适配手机和桌面的网页入口。Android 与保留的 SwiftUI iOS 客户端继续使用原接口、JSON 字段和 `8787` 端口。旧 Python 代码与 SQLite 文件保留供对照和回退；新后端不运行 Python，也不会自动导入旧 SQLite 数据。**当前仍为本机单用户原型，未部署线上服务，也未提交 HackOS。**

完整迁移范围与代码阅读说明见 [迁移说明](docs/ASPNET-MIGRATION.md)。

## 功能与边界

- 悉尼市区 1–3 天步行规划，8 个限定候选地标，每天最多 4 站，跨天不重复。
- Nemotron 只对已知地点 ID 排序；C# 代码独立检查预算、步数、营业时间、休息、衔接和返程。
- 调整先生成草案，用户确认后才保存。保存使用版本检查，冲突返回 HTTP `409`。
- 多行程管理、逐日天气刷新，以及需用户同意的偏好保存和撤回。
- Google Places 提供坐标与常规营业时段，Routes 提供步行路线，Weather 提供未来 10 天逐日预报。
- Android 原有引导、准备清单与整数分记账保留；这些设备端功能不等于网页已实现相同功能。

## 本机启动：PostgreSQL + .NET API

需要 **.NET 10 SDK**、**Node.js 24** 和 **Docker / Docker Compose**。Node 用于网页构建和官方 Meterless H-MEM 引擎；HTTP API、规划与天气逻辑由 C# 运行。

在包含本次迁移的仓库根目录运行：

```sh
docker compose up -d db
```

Compose 的本机开发数据库为 `localhost:55432`，数据库名 `sydneybuddy`，用户名为 `sydneybuddy`，密码为 `sydneybuddy_dev`。这些值只适用于本机开发。按终端设置连接字符串，再启动 API：

**Windows PowerShell**

```powershell
$env:ConnectionStrings__SydneyBuddy = 'Host=localhost;Port=55432;Database=sydneybuddy;Username=sydneybuddy;Password=sydneybuddy_dev'
dotnet run --project server-dotnet/SydneyBuddy.Api
```

**macOS / Linux**

```sh
export ConnectionStrings__SydneyBuddy='Host=localhost;Port=55432;Database=sydneybuddy;Username=sydneybuddy;Password=sydneybuddy_dev'
dotnet run --project server-dotnet/SydneyBuddy.Api
```

首次运行会还原 NuGet 依赖，API 启动时使用 EF Core migrations 创建或更新 PostgreSQL 表。先关闭旧 Python 后端，避免争用 `8787` 端口。服务运行后打开 [健康检查](http://127.0.0.1:8787/health) 或 [本机密钥设置页](http://127.0.0.1:8787/setup)。每位成员使用自己的本机数据库；这仍是单用户开发模式。

也可通过启动脚本运行：Windows 使用 `powershell -File scripts/start-server.ps1`；macOS / Linux 使用 `sh scripts/start-server.sh`；macOS 也可双击 `scripts/start-server.command`。连接字符串仍需先设置在对应进程的环境中。

## 网页入口：React + TypeScript

另开终端，在仓库根目录运行：

```sh
npm --prefix web install
npm --prefix web run dev
```

打开 Vite 输出的网址。开发服务器将 `/api` 请求代理到 `http://127.0.0.1:8787`，因此 .NET API 需要保持运行。该入口适配桌面与手机浏览器；手机访问另一台电脑上的服务属于额外网络部署，不由本机回环配置自动完成。

构建后可由 ASP.NET Core 提供网页文件：

```sh
npm --prefix web run build
dotnet run --project server-dotnet/SydneyBuddy.Api
```

构建输出在 `web/dist`。如果 API 在构建前已经启动，请先停止并重新启动 API，让它发现网页资源。然后从 [API 的本机地址](http://127.0.0.1:8787) 打开网页。没有构建产物时使用 Vite 开发服务器。

## Android（Windows / macOS）

额外需要 Android Studio、Android SDK 35、JDK 17 或 21，以及带 Google Play 服务的 Android 8.0+ 模拟器或手机。

1. 先按上文启动 PostgreSQL 和 .NET API。
2. Android Studio 打开 **`android/`**，等待 Gradle 同步。
3. 连接已开启 USB 调试的手机，或启动模拟器。
4. Windows 运行 `powershell -File scripts/connect-android.ps1`；macOS 运行 `sh scripts/connect-android.sh`，将设备端口转发到电脑。
5. 在 Android Studio 运行 `app`。不配置密钥即可测试演示流程，地图会明确标注为示意图。

每次重启模拟器或重新连接手机后，需要重做端口转发。若 `adb` 不在 PATH，连接脚本会尝试从 `ANDROID_HOME` 或默认 SDK 路径查找。命令行构建在 `android/` 中执行：

```sh
# macOS / Linux；Windows 使用 .\gradlew.bat
sh gradlew :app:assembleDebug :app:testDebugUnitTest :app:lintDebug
```

APK 输出为 `android/app/build/outputs/apk/debug/app-debug.apk`。真实 Google 底图需要单独的 Maps SDK for Android 密钥，写入被忽略的 `android/local.properties`：`MAPS_API_KEY=你的Android地图密钥`；保留 Android Studio 生成的 `sdk.dir`。iOS 地图密钥不通用，具体密钥限制由项目管理员管理。

Android 仍连接 `http://127.0.0.1:8787`。Debug 允许本机 HTTP，Release 禁止明文连接。独立联网 APK 需要 HTTPS 后端、客户端地址与身份认证；本次技术栈迁移不包含应用商店发布或云端部署。现有 [Android 验证记录](docs/ANDROID-VERIFICATION.md) 属于迁移前的历史证据。

## 保留的 iOS 客户端

需要 macOS、Xcode（iOS 17+ SDK/模拟器）、.NET 10 SDK 和 Node.js 24。先启动 PostgreSQL 和 .NET API，再打开 `ios/SydneyBuddy.xcodeproj`，运行 `SydneyBuddy` scheme。首次 Xcode 构建需联网下载 Google Maps SDK。

同机模拟器继续连接 `127.0.0.1:8787`；iPhone 真机不能用其自身的回环地址访问电脑。iOS 地图密钥更改后需要重新构建。原 [iOS 验证记录](docs/VERIFICATION.md) 是迁移前的历史记录，不能替代新版验证。

## 配置

仓库只提供配置模板，不包含可用服务密钥。服务端读取仓库根目录 `.secrets.json` 或同名环境变量，环境变量优先；不会自动加载 `.env`。Node H-MEM 数据继续保存在 `server/.hmem/`。

| 配置项 | 用途 |
|---|---|
| `ConnectionStrings__SydneyBuddy` | PostgreSQL 连接字符串 |
| `NVIDIA_API_KEY` | 服务端调用 NVIDIA NIM，不进入 APK 或网页源码 |
| `NVIDIA_MODEL` | 默认 `nvidia/nemotron-3.5-lightning-30b-a3b` |
| `GOOGLE_SERVER_KEY` | 服务端 Places API (New)、Routes API、Weather API |
| `GOOGLE_IOS_KEY` | 保留的 iOS 客户端地图 SDK 配置 |
| Android `MAPS_API_KEY` | 仅写入 `android/local.properties` 的 Android 地图 SDK 密钥 |
| `NODE_BINARY` | 可选，Node 24 可执行文件路径；默认从 PATH 查找 |
| `SYDNEYBUDDY_ROOT` | 可选，仓库根目录绝对路径，自动定位失败时设置 |

API 密钥也可通过本机 `/setup` 页面保存。模板见 [.secrets.example.json](.secrets.example.json) 和 [Secrets.example.xcconfig](ios/Config/Secrets.example.xcconfig)。单独设置 iOS 环境变量不会自动生成客户端构建配置；应通过设置页或手动填写 xcconfig。

Google 项目需要启用计费及对应服务。地图 SDK 与服务端 API 分别配置。`/health` 显示已配置，不代表模型、地图、路线或天气真实调用成功；费用参考 [Google 官方价格](https://developers.google.com/maps/billing-and-pricing/pricing)。

## 模式和天气

| 模式 | 模型 | 其他数据 |
|---|---|---|
| `demo` | 本地规则，不调用 AI | 示例地点、天气、营业时段与距离估算 |
| `assisted` | 真实 Nemotron | 示例地点、天气和路线，不请求 Google 服务端 API |
| `live` | 真实 Nemotron | Google Places、Routes、Weather；花费、场内步数与停留时间仍为估算 |

天气按悉尼旅行日期逐日匹配，并分别影响当天室内/室外筛选。Weather 请求未来 10 天、`pageSize=10`，超时 8 秒；失败或超出范围时明确显示未知。独立刷新天气不会重排已保存行程，调整仍需生成并确认草案。雨天快捷情景明确标注为用户指定，不能当成实时天气证据。

## API 兼容性

本机服务仍使用 `127.0.0.1:8787`，无账户系统，保留本机来源检查。JSON 请求使用 `Content-Type: application/json`。

| 方法 / 路径 | 说明 |
|---|---|
| `GET /health` | 服务状态与配置是否存在，不返回密钥 |
| `GET /catalog` | 8 个候选地点的示例基础数据 |
| `POST /plan` | 按 profile 和 mode 生成草案，不保存 |
| `POST /weather` | 刷新天气，不保存或重排行程 |
| `GET /trips` | 已保存行程 |
| `POST /trips` | `{ "trip": <草案>, "expectedRevision": 0 }`；修改时传当前 revision |
| `DELETE /trips/{id}` | 删除指定行程 |
| `GET /memories` | 已保存的授权偏好 |
| `POST /memories` | `{ "consent": true, "summary": "喜欢艺术", "profile": {"interests":["艺术"],"maxSteps":5000} }` |
| `DELETE /memories/{id}` | 从可检索记忆撤回，保留追加审计历史 |
| `GET/POST /setup` | 本机配置页，提交需要页面提供的 CSRF 令牌 |

网页通过 `/api` 前缀访问对应接口；Android / iOS 保留上述原路径。约束无可行解为 `422`，保存版本冲突为 `409`，非法来源为 `403`。API 不执行订票、付款或赛事提交。

## 测试与 CI

```sh
dotnet restore server-dotnet/SydneyBuddy.Api.Tests/SydneyBuddy.Api.Tests.csproj
dotnet build server-dotnet/SydneyBuddy.Api.Tests/SydneyBuddy.Api.Tests.csproj --no-restore
dotnet test server-dotnet/SydneyBuddy.Api.Tests/SydneyBuddy.Api.Tests.csproj --no-build
npm --prefix web run build
npm --prefix web test
# macOS / Linux：官方 H-MEM conformance
HMEM_IMPL="$PWD/server/vendor/hmem/src/index.ts" \
  node --experimental-transform-types server/vendor/hmem/conformance/runner.ts
```

后端集成测试在上述 PostgreSQL 连接中创建专属随机 schema，完成后清理；连接用户需要创建 schema 的权限。详细说明见 [迁移说明](docs/ASPNET-MIGRATION.md)。GitHub Actions 启动 PostgreSQL 服务，使用 .NET 10 构建与测试，运行 H-MEM 检查和网页构建。Android 继续使用原构建工作流。自动测试不调用付费 API。迁移后的测试结果以当前 CI 和迁移说明为准；原 Python / Android / iOS 版本的成功记录不能直接作为新版在线或真机验收结果。

## 项目结构

| 路径 | 职责 |
|---|---|
| `web/` | React / TypeScript 响应式网页与 Vite 构建 |
| `android/` | Kotlin / Jetpack Compose Android 客户端 |
| `ios/` | 保留的 SwiftUI 客户端 |
| `server-dotnet/SydneyBuddy.Api/` | ASP.NET Core REST API、C# 规划、天气、依赖注入、EF Core / PostgreSQL 存储 |
| `server-dotnet/SydneyBuddy.Api.Tests/` | 新后端的单元与 HTTP 兼容性测试 |
| `server/places.json` | 沿用的候选地点基础数据 |
| `server/hmem_bridge.ts`、`server/vendor/hmem/` | 由 C# 调用的官方 H-MEM 引擎与许可 |
| `server/.hmem/` | 本机记忆与审计数据，Git 忽略目录 |
| `server/*.py`、`server/tests/` | 旧 Python 实现和对照测试，不参与新后端运行 |
| `compose.yaml` | 本机 PostgreSQL 开发服务 |

## 限制与黑客松状态

- 限定 8 个悉尼地标、步行模式，每天最多尝试 8 个初筛候选顺序，不保证全局最优。Google 常规营业时段可能不适用于节假日。
- 花费、步数和停留时间为估算；没有住宿、全部餐饮、实时票价、排队、坡度或无障碍模型。
- 没有多人账户、共享同步、支付、订票、酒店预订、航班追踪或后台 GPS。PostgreSQL 支持存储，并不自动提供用户隔离或云端同步。
- H-MEM 为本机单用户，撤回后不再检索，但审计历史保留；未启用自动 dreaming / sleep。
- 原 Python 版本有 NVIDIA NIM 和 H-MEM 验证记录；迁移后的真实在线功能需使用本机配置重新验收。Cursor、Zetaris 按团队原要求暂缓，Nightshift 未接入；不声称满足全部赛事要求。见 [参赛清单](docs/submission-checklist.md)。
- `docs/` 中既有截图与视频描述迁移前版本；网页和 .NET 后端以当前代码和测试结果为准。

## 来源与许可

项目沿用 [MIT 许可证](LICENSE)。`server/vendor/hmem` 保留官方参考实现的 Apache-2.0 许可和 [来源说明](server/vendor/hmem/UPSTREAM.md)。Google SDK 及在线服务受各自条款约束。

[Google Maps iOS](https://developers.google.com/maps/documentation/ios-sdk/config) · [Places](https://developers.google.com/maps/documentation/places/web-service/text-search) · [Routes](https://developers.google.com/maps/documentation/routes/compute_route_directions) · [Weather](https://developers.google.com/maps/documentation/weather/overview) · [NVIDIA NIM](https://docs.api.nvidia.com/nim/reference/llm-apis) · [Meterless](https://github.com/Meterless/Meterless/tree/main/engines/hmem)

交互借鉴 Duolingo 的小步学习、即时反馈与完成奖励，使用原创 Buddy 角色；本项目与 Duolingo 无关联。
