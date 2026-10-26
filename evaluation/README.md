# 推荐与路线算法独立评测

这是一个不依赖 iOS、数据库或生产 API 的后端评测入口。它直接复用 `Planner`、`RecommendationService`、`RouteScheduler`、`TravelConstraints` 和 `RemainingJourney`；不会实现第二套推荐或排程逻辑。

## 运行

在仓库根目录执行：

```sh
dotnet run --project server-dotnet/SydneyBuddy.Evaluation
```

每次运行会在 `evaluation/output/<UTC 时间戳>/` 生成：

- `results.csv`：逐案例、逐策略的结果、地点数、步数、费用、未核实信息和失败原因；
- `report.html`：需求、地点顺序、时间线和问题的轻量可读报告；
- `issues.md`：最多 3 项需要产品/后端讨论的发现，含案例、输入、实际结果、期望和对应模块；
- `catalog-snapshot.csv`：本轮所用地点目录快照；
- `cases/`：每个策略的完整输入/输出 JSON，便于复核。

输出目录被 Git 忽略；案例和输入材料保存在 `evaluation/data/`。

## 两种对比策略

默认运行 **A: local_rule**，即现有 `demo` 模式的本地推荐与确定性规则排程。它从不访问外部 API。

报告同时列出 **B: ai_plus_rules**，但若未显式启用，状态为 `not_run`。这不是失败伪装成 AI 运行，也不会写入模拟模型结果。

只有已经在本机配置 `NVIDIA_API_KEY` 时，才可运行真实 AI 对比：

```sh
dotnet run --project server-dotnet/SydneyBuddy.Evaluation -- --with-ai
```

该模式使用当前 `RecommendationService` 请求 NVIDIA，再让同一套 `RouteScheduler` 验证预算、步数、营业时间、休息和返程。评测固定候选目录与用户输入；外部模型结果和报告会保存为本轮证据。不要把默认演示估算、未配置模型，或缺少实时道路资料的情形写成“AI 已执行”。

## 输入与证据边界

- `cases.json` 固定 10 个虚拟用户案例：少走、美食、低预算、雨天、多天、必去/避开、途中疲劳后重算、候选不足与无可行路线。
- `user-habits.csv` 是案例偏好/精力参数的可审计索引；运行器要求每个案例都有一行。
- `expected-checks.csv` 是逐案例的预期检查表（成功/明确失败、必去/避开、预算、步数、留白和错误提示）。运行器会验证它与 `cases.json` 的案例 ID 及结果类型一一对应；具体断言由同一案例 JSON 执行，避免 CSV 字段表达能力不足时静默丢失约束。
- 地点事实来自当前 `server/places.json`，并在每次运行导出为 `catalog-snapshot.csv`。费用、营业时间及其来源会保留在输出中。
- `transport-evidence.json` 明确默认没有道路/公交回放。因此默认路线是演示估算；E08 断言没有 Apple 步行证据时，途中重算必须明确停止，不能用直线距离冒充真实导航。

若以后加入 Apple MapKit 或 TfNSW 的固定回放，需记录采集时间、查询时间、来源和真实/模拟标识；实时 API 测试应与固定案例评测分开运行。
