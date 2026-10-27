# 推荐与路线算法独立评测

这是一个默认不依赖 iOS、数据库或实时生产 API 的后端评测入口。它直接复用 `Planner`、`RecommendationService`、`RouteScheduler`、`TravelConstraints` 和 `RemainingJourney`；不会实现第二套推荐或排程逻辑。默认会验证已录制的 Apple 步行和 TfNSW 公交固定快照；只有显式刷新 TfNSW 快照时才会请求官方 API。

## 运行

在仓库根目录执行：

```sh
dotnet run --project server-dotnet/SydneyBuddy.Evaluation
```

每次运行会在 `evaluation/output/<UTC 时间戳>/` 生成：

- `results.csv`：逐案例、逐策略的结果、地点数、步数、费用、未核实信息和失败原因；
- `report.html`：需求、地点顺序、时间线和问题的轻量可读报告；
- `issues.md`：最多 3 项需要产品/后端讨论的发现，含案例、输入、实际结果、期望和对应模块；
- `tfnsw-transit-replay.json`：独立 T01 固定公交回放的来源、时刻连续性、公共交通步骤和排程消费结果；
- `catalog-snapshot.csv`：本轮所用地点目录快照；
- `cases/`：每个策略的完整输入/输出 JSON，便于复核。

输出目录被 Git 忽略；案例和输入材料保存在 `evaluation/data/`。

## 两种对比策略

默认运行 **A: local_rule**，即现有 `demo` 模式的本地推荐与确定性规则排程。它从不访问外部 API。

报告同时列出 **B: ai_plus_rules**，但若未显式启用，状态为 `not_run`。`not_run` 既不是“通过”，也不会被计入通过数量或显示为绿色；这不是失败伪装成 AI 运行，也不会写入模拟模型结果。E08 是 `RemainingJourney` 的固定道路回放，模型并不参与选点，因此 AI 行显示 `not_applicable`，同样不计为通过或失败。

只有已经在本机配置 `NVIDIA_API_KEY` 时，才可运行真实 AI 对比：

```sh
dotnet run --project server-dotnet/SydneyBuddy.Evaluation -- --with-ai
```

该模式使用当前 `RecommendationService` 请求 NVIDIA，再让同一套 `RouteScheduler` 验证预算、步数、营业时间、休息和返程。评测固定候选目录与用户输入；外部模型结果和报告会保存为本轮证据。不要把默认演示估算、未配置模型，或缺少实时道路资料的情形写成“AI 已执行”。显式传入 `--with-ai` 时，只要 AI 未运行或任一 AI 案例断言失败，进程会以非零退出码结束。

## 输入与证据边界

- `cases.json` 固定 10 个虚拟用户案例：少走、美食（要求实际安排具体用餐地点）、低预算、雨天、多天不重复、必去/避开、途中疲劳后的固定道路回放重算、候选不足与无可行路线；成功案例还检查每日终点是否为用户指定终点。
- `user-habits.csv` 是案例偏好/精力参数的可审计索引；运行器要求每个案例都有一行。
- `expected-checks.csv` 是逐案例的预期检查表（成功/明确失败、必去/避开、预算、步数、留白和错误提示）。运行器会验证它与 `cases.json` 的案例 ID 及结果类型一一对应；具体断言由同一案例 JSON 执行，避免 CSV 字段表达能力不足时静默丢失约束。
- 地点事实来自当前 `server/places.json`，并在每次运行导出为 `catalog-snapshot.csv`。费用、营业时间及其来源会保留在输出中。
- `apple-mapkit-walking-replay.json` 是 E08 的默认道路回放：2026-10-26 由 iOS Simulator 中的 `MKDirections.Request.transportType = .walking` 对六个固定公开地标方向逐段查询并导出。它含采集时间、Sydney 日期/时区、精确公开端点、距离、ETA，并明确 `usesDeviceLocation: false` 和 `notLiveNavigation: true`。第六段植物园→岩石区是 Apple 直接返回的路线，而不是经歌剧院拼接的替代品。运行器会验证 provider、时间、端点、六条路段以及疲劳后真正采用该直接路段；生产入口仍要求六小时内的新查询，历史开关只在评测/测试代码中显式启用。
- `recorded-walking-replay.json` 保留为 Valhalla/OpenStreetMap 的历史回归样本，需显式传入 `--walking-replay evaluation/data/recorded-walking-replay.json` 才会使用。它的植物园→岩石区连接段仍明确标为 `composed_historical_path`，不会被改叫 Apple 或实时导航。
- `transport-evidence.json` 是独立 T01 的 TfNSW 历史公交快照：固定公开起点 Central Station、终点 Circular Quay、Sydney 日期/分钟、时区和采集时间；路线必须含至少一段公共交通、可连续的班次时刻及官方 `Transport for NSW / Trip Planner` 来源。运行器会先用 `TransportEvidence` 按端点/日期/分钟查找它，再把同一份快照交给 `ScheduleEditorService`，确认排程结果实际使用 `transit` 路段，而非退化成演示步行估算。快照不含帐户、设备位置、请求头或密钥，且明确标为历史固定回放、非实时班次／导航；票价仍未知。
- E08 先断言移除道路证据会明确停止，原方案则因可选的 210 分钟歌剧院停留超过疲劳后的 7,000 步上限；随后以固定的 `currentMinute=640` 重算，自动移除该可选停留，并检查已完成活动保持、未来站点不重复、步数上限和无阻塞问题。

## 正确解读结果与当前缺口

- 报告中的 **通过** 表示路线输出满足该案例断言；**符合预期：明确拒绝** 表示不可行/缺证据时安全停止；**未运行** 表示没有证据，绝不能算作通过；只有 **未通过** 才会进入 `issues.md` 的实际失败列表。
- E08 覆盖的是**有限候选**的历史 Apple MapKit 道路回放下的疲劳后成功重算：它不让缺证据时退化为直线距离，疲劳后也只会采用已录制的植物园→岩石区直接路段。它不证明实时导航、全图替代路线搜索、任意重排或公交选择。
- 默认运行验证六段 Apple MapKit 历史步行快照，以及一条 TfNSW 历史公交固定快照的公共交通步骤、时间连续性和排程消费；它仍不验证实时 TfNSW 班次、票价、营业时间或排队，也不覆盖所有悉尼路线。模型策略仍须显式配置密钥并以 `--with-ai` 跑出真实模型结果。
- 因此这是一套已能防回归的本地规则评测入口，不是“推荐和路线算法已经验证完成”的结论。

若重新录制 Apple MapKit 快照，请使用 Debug 专用启动参数，绝不从 `UserDefaults` 缓存倒取数据：

```sh
xcodebuild -project ios/SydneyBuddy.xcodeproj -scheme SydneyBuddy -configuration Debug -sdk iphonesimulator -destination 'platform=iOS Simulator,id=<simulator-id>' -derivedDataPath /private/tmp/sydneybuddy-mapkit-build build
xcrun simctl install <simulator-id> /private/tmp/sydneybuddy-mapkit-build/Build/Products/Debug-iphonesimulator/SydneyBuddy.app
xcrun simctl launch --terminate-running-process <simulator-id> com.sydneybuddy.travel --export-mapkit-walking-replay
```

审核 Simulator app Documents 中导出的 JSON（不得含用户位置、帐户或密钥）后，才可替换 `apple-mapkit-walking-replay.json`。

若要刷新 TfNSW 固定快照，先仅在本机 `/setup` 保存已获授权的 `TFNSW_API_KEY`，再运行：

```sh
dotnet run --project server-dotnet/SydneyBuddy.Evaluation -- \
  --capture-tfnsw-replay --transit-date 2026-10-27 --transit-minute 780
```

这会向官方 Trip Planner 查询固定的公开 Central Station→Circular Quay 区间，覆盖 `evaluation/data/transport-evidence.json`，并立刻执行 T01 和其余评测。命令不会打印、导出或提交密钥；提交前仍应确认快照没有帐户、设备位置、请求头或密钥。实时 API 冒烟测试应与这个固定案例评测分开运行。
