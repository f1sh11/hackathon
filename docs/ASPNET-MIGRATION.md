# Sydney Buddy：原生 iOS / Android 与 ASP.NET Core 后端

本次变更提供 C# / ASP.NET Core（.NET 10）API、EF Core / PostgreSQL 行程存储，供原生 iOS / Android 客户端使用。React 网页客户端与独立旧原型已移除。Android 的 Kotlin / Compose 和保留的 SwiftUI iOS 客户端继续使用原 API 合约。

## 迁移前后

| 部分 | 原实现 | 本次实现 |
|---|---|---|
| HTTP API | Python `http.server` | ASP.NET Core Minimal API |
| 规划和约束检查 | `server/planner.py` | C# `Planning/Planner.cs` 和 `PlanValidator.cs` |
| 天气查询 | `server/weather.py` | C# `Services/WeatherService.cs` |
| 行程和偏好摘要 | 电脑本机 SQLite | EF Core + Npgsql PostgreSQL provider |
| 官方 H-MEM | Python 调用 Node 24 | C# 调用原 Node 24 / TypeScript 引擎 |
| Android / iOS | 原生客户端 | 保留原代码和接口格式 |

新后端没有 Python 运行依赖，H-MEM 仍使用官方引擎。保留其 TypeScript 入口和来源许可，有助于继续运行官方 conformance 检查。

本次没有加入账户系统、多人权限、订票支付、云端托管或应用商店发布。数据库更换也不等于已经实现共享行程或用户隔离。

## 启动步骤

1. 安装 .NET 10 SDK、Node.js 24、Docker / Docker Compose。
2. 在仓库根目录运行 `docker compose up -d db`。
3. 设置 `ConnectionStrings__SydneyBuddy`，本机 Compose 的连接值见 [README](../README.md)。API 启动时在独立 DI scope 中执行 EF Core `Database.MigrateAsync()`，应用仓库中的数据库迁移。
4. 关闭占用 `8787` 的旧 Python 服务，再运行 `dotnet run --project server-dotnet/SydneyBuddy.Api`。
5. 打开 `http://127.0.0.1:8787/health`。使用原 `.secrets.json`，或在本机 `/setup` 页面配置服务端密钥。
6. Android 按原流程构建，运行 `adb reverse tcp:8787 tcp:8787` 后连接同一个 .NET 服务；iOS 同机模拟器仍连接原地址。

服务从当前目录及程序目录的父级查找仓库根目录。若从自定义位置运行或自动定位失败，设置 `SYDNEYBUDDY_ROOT` 为仓库根目录的绝对路径。发布目录仍需能够访问地点 JSON 和 H-MEM 资源；本次未提供独立云端发布包。

## 数据和配置处理

- PostgreSQL 使用本机独立数据库，连接字符串通过 `ConnectionStrings__SydneyBuddy` 配置。Compose 数据使用持久化 volume；正常停止使用 `docker compose down`，不会删除 volume。
- 原 `server/state.sqlite3` 及其附属文件保留，不会被新后端覆盖、转换或自动导入。升级后列表从 PostgreSQL 读取，因此旧 SQLite 行程不会自动出现。迁移真实旧数据之前，应另行备份并核对导入方案。
- 原 `server/.hmem/` 仍用于官方记忆引擎。PostgreSQL 保存供客户端列出的偏好摘要，H-MEM 保存检索和审计状态；两者职责不同。原有 H-MEM 内容可能继续参与检索，但旧 SQLite 的偏好摘要不会自动加入 PostgreSQL 列表。
- 配置仍读取根目录 `.secrets.json` 和同名环境变量；环境变量优先。服务端密钥不会加入 Android APK、iOS App 或 API 健康响应。项目不自动加载 `.env`。
- 若需要对照旧实现，先停止 .NET 服务，使用 Python 运行原 `server/app.py`；它读写自己的旧 SQLite 数据，与 PostgreSQL 数据分开。不要同时启动两个后端争用同一端口。

## API 合约

Android / iOS 保留 `GET /health`、`GET /catalog`、`POST /plan`、`POST /weather`、`GET/POST /trips`、`DELETE /trips/{id}`、`GET/POST /memories`、`DELETE /memories/{id}` 和本机 `/setup`。

仅注册手机端使用的原路径。React 静态文件托管和旧网页的 `/api` 别名已移除；根路径 `/` 返回 JSON 404，`/setup` 配置页保留。保留以下行为：

- 规划只产生草案，`POST /trips` 才保存。
- 新建提交 `expectedRevision: 0`；修改提交当前 revision。PostgreSQL 使用原子版本检查，冲突返回 `409`，不会静默覆盖另一份更新。
- 输入与业务约束失败分别返回合适错误；无法安排路线为 `422`。错误 JSON 使用客户端认识的 `error` 字段。
- 没有配置或外部服务失败时，不会将示例数据声称为实时结果。天气缺失、超范围或失败有明确状态。
- 用户同意后才保存偏好；撤回停止后续检索，但官方追加审计历史仍保留。

## 代码职责和数据流

| 文件 / 目录 | 主要责任 |
|---|---|
| `server-dotnet/SydneyBuddy.Api/Program.cs` | 应用启动、依赖注入注册、数据库配置、手机 API 注册 |
| `Api/ApiEndpoints.cs` | REST API 路由协调，将功能注册到手机使用的原路径 |
| `Api/PlanningEndpoints.cs`、`TripEndpoints.cs` | 规划 / 天气与行程集合的 HTTP 合约 |
| `Api/MemoryEndpoints.cs`、`SettingsEndpoints.cs` | 授权偏好与本机配置的 HTTP 合约 |
| `Api/RequestJson.cs`、`TripContractValidator.cs` | JSON 请求读取和持久化前的完整行程校验 |
| `Api/LocalRequestMiddleware.cs` | 本机访问边界、响应头、错误 JSON 和请求日志 |
| `Infrastructure/ProjectPaths.cs`、`SecretStore.cs` | 资源目录定位与服务端私有配置 |
| `Infrastructure/DependencyInjection.cs` | 集中注册服务及 scoped / singleton 生命周期 |
| `Data/BuddyDbContext.cs`、`Data/Migrations/` | EF Core 模型映射、JSONB 列、revision 约束及版本化数据库迁移 |
| `Data/TripEntity.cs`、`MemoryEntity.cs` | 行程和偏好数据库实体 |
| `Data/TripRepository.cs` | LINQ 查询、异步保存、条件更新和 revision 冲突 |
| `Data/MemoryRepository.cs` | 授权偏好摘要的异步查询、保存和删除 |
| `Planning/PlanValidator.cs` | 日期、预算、步数、时间与起终点输入校验 |
| `Planning/Planner.cs` | 编排校验、证据收集、推荐和调度，返回草案 |
| `Planning/PlaceCatalog.cs`、`CatalogCache.cs` | 有容量限制的本地公共景点目录缓存 |
| `Planning/PlaceService.cs` | 示例或 Google Places 候选信息收集 |
| `Planning/RecommendationService.cs` | H-MEM 授权偏好和 Nemotron 候选排序 |
| `Planning/RouteService.cs` | 步行路段查询、距离估算和本次规划内复用 |
| `Planning/RouteScheduler.cs` | 多日候选安排、营业窗口与整条路线约束 |
| `Planning/PlanningContext.cs` | 一次规划的配置快照、trace 和路段缓存，不跨请求共享 |
| `Services/WeatherService.cs`、`ExternalJsonClient.cs` | 天气匹配、外部 JSON 请求、超时和错误处理 |
| `Services/MemoryService.cs` | Node 子进程生命周期与官方 H-MEM 调用 |
| `Services/PreferenceService.cs` | 协调 H-MEM 与数据库摘要，保存数据库失败时尝试撤回刚添加的记忆 |

表中除 `Program.cs` 外，C# 路径均相对于 `server-dotnet/SydneyBuddy.Api/`。

用户在手机端提交偏好后，HTTP 请求进入 API endpoint。输入校验通过后，Planner 收集候选、读取天气并在对应模式下调用 Nemotron；程序随后逐段验证路线和约束，返回草案。用户确认保存时才调用 TripRepository，经 EF Core 写入 PostgreSQL，并用 revision 阻止旧版本覆盖。偏好记忆经 PreferenceService 协调：MemoryService 调用原 TypeScript 引擎，MemoryRepository 保存 PostgreSQL 摘要；后者失败时尝试撤回刚添加的记忆。这是补偿机制，不是跨两个存储的数据库事务；若补偿也失败，日志会提示人工核对。

## 建议亲自阅读的三处

1. **`Api/PlanningEndpoints.cs` 与 `TripEndpoints.cs`**：理解“生成草案”和“保存”的分界，以及 JSON 如何从客户端进入 C#。
2. **`Planning/Planner.cs` 与 `RouteScheduler.cs`**：先看编排，再看调度和最终验证；理解为什么模型只排序已知地点，预算、步数、营业窗口和返程由程序独立检查。
3. **`Data/TripRepository.cs` 的 revision 保存逻辑**：理解 EF Core 的 LINQ 条件更新如何在 PostgreSQL 并发写入时返回冲突。可在测试中尝试给同一行程重复提交旧 revision，观察第二次保存的 `409`。

这三处分别对应 HTTP / async-await / 依赖注入、业务规则，以及 ORM 与数据库并发控制。修改业务约束时从 RouteScheduler 入手，修改 HTTP 字段时从 endpoint 和 Android / iOS 请求层一起核对。


## 工程概念与实现位置

| 概念 | 在这个项目中的用途 |
|---|---|
| C# 小类与职责分离 | Endpoint 处理 HTTP，Planner 编排，服务收集外部证据，Repository 处理数据库 |
| ASP.NET Core Web API / REST | `GET` 查询、`POST` 生成或保存、`DELETE` 删除；以 JSON 和 HTTP 状态表达结果 |
| async / await | 外部 HTTP、数据库和 H-MEM 子进程等待时释放请求线程，传递 CancellationToken |
| 依赖注入（DI） | Program 调用 DependencyInjection 注册；DbContext / Repository 随请求创建，公共目录缓存为 singleton，构造函数声明依赖；测试可替换外部服务 |
| EF Core | DbContext 和实体维护 PostgreSQL 映射，Repository 通过 ORM 访问数据库，启动时应用版本化 migrations |
| LINQ | 筛选和投影实体、组织候选和路线；EF 查询在异步执行时转为数据库请求 |
| 内存缓存 | 仅复用本地公共景点目录，TTL 5 分钟，独立缓存容量为 32 个计量单位，每份目录占 1；返回深拷贝 |
| 每次规划内的路段复用 | PlanningContext 保存本次路线查询结果，避免候选调度重复查询同一路段；请求结束后释放 |
| 结构化日志 | LocalRequestMiddleware 记录 method、路由模板、status、耗时和 TraceId；内部失败仅记录异常类型，不记录请求正文、密钥或提供商原始响应 |
| 前后端分离 | Android / iOS 通过 HTTP JSON 调用 API；C# 负责规划、外部服务端密钥和数据库 |

缓存不涵盖天气响应、模型输出、私有偏好或服务端密钥。地点目录的 5 分钟缓存是本地资源优化，不代表实时 Google 数据可使用 5 分钟内的旧结果。

## 调试入口

| 现象 | 先检查 |
|---|---|
| API 启动失败或连不上数据库 | PostgreSQL 是否健康、连接字符串、Compose 端口 `55432` |
| 端口已占用 | 是否仍有 Python 或另一份 .NET 服务占用 `8787` |
| 启动找不到地点或 H-MEM 文件 | 仓库目录定位，必要时设置 `SYDNEYBUDDY_ROOT` |
| Android 无法连接 | 后端进程、目标设备以及 `adb reverse` |
| 行程返回 `422` | 输入范围、营业窗口、预算、步数和返程时间 |
| 保存返回 `409` | 重新加载当前行程，基于最新 revision 再确认 |
| 记忆调用失败 | Node 24、`NODE_BINARY`、H-MEM 目录可写性 |
| 实时天气或规划失败 | API 服务配置、外部服务权限与额度；不要输出密钥或原始敏感响应 |

## 验证和验收

后端测试项目为 `server-dotnet/SydneyBuddy.Api.Tests/SydneyBuddy.Api.Tests.csproj`。运行方式：

```sh
dotnet restore server-dotnet/SydneyBuddy.Api.Tests/SydneyBuddy.Api.Tests.csproj
dotnet build server-dotnet/SydneyBuddy.Api.Tests/SydneyBuddy.Api.Tests.csproj --no-restore
dotnet test server-dotnet/SydneyBuddy.Api.Tests/SydneyBuddy.Api.Tests.csproj --no-build
```

CI 提供 PostgreSQL 服务与连接字符串，执行后端测试和官方 H-MEM conformance。ApiFixture 在连接字符串指定的数据库中创建 `sydney_tests_` 前缀的随机 schema，通过 SearchPath 隔离测试表和迁移历史，结束后删除该 schema；测试用户需要创建 schema 的权限。集成测试使用真实 PostgreSQL，外部付费服务使用替身，缺少数据库时会明确失败。旧 Python 测试保留作迁移行为对照，不是新后端的 Python 运行依赖。

本说明不预填测试数量或成功结论，实际结果应来自本次构建日志与 CI。交付前需要区分：后端自动测试成功、Android 或 iOS 真实设备连通、以及真实 Google / NVIDIA 在线调用。`/health` 看到配置存在只能证明配置已读取，不能替代付费 API 的实际成功响应。
