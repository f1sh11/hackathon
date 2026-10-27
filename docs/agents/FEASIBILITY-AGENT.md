# 独立可行性检查 Agent：队友对接说明

这是已实现的后端模块。不是三个 Agent 已全部完成的声明；需求 Agent、规划 Agent 由队友通过以下契约接入。未更改 iOS 的页面布局。

## 责任与权限

`FeasibilityAgent` 有独立角色提示词、上下文、受限的工具选择循环和检查结果。与规划 Agent 共用模型 provider，不共用角色上下文。

- 能独立选择 `route_evidence`、`place_evidence`、`refresh_transit` 工具，最多五次模型决策，总模型预算 150 秒。
- `constraint_check` 是强制程序校验，由 PolicyGuard 执行。模型不能跳过或覆盖它。明确硬性冲突立即返工，不为算术问题额外等待模型；此时 modelReviewRequired=false，modelReviewCompleted=false，不伪称模型已执行。
- 可以返回 pass / revise / needs_info / unavailable。硬性冲突一定阻止确认；模型未完成时不能 pass。
- 可以针对已有节奏密度证据要求返工，或基于价格/营业未知要求补资料。不能编造问题或证据编号。
- 不修改行程、长期记忆和用户约束。返工通过结构化 handoff 返回，不假装已经调用尚未接入的另两个 Agent。

## 请求：POST /agents/feasibility/reviews

```json
{
  "planId": "team-draft-001",
  "planRevision": 1,
  "requirementsRevision": 1,
  "trip": "这里替换成现有 /plan 返回的完整 JSON 对象",
  "requirements": "可选：已确认需求对象，默认使用 trip.profile"
}
```

已保存行程也可传 `tripId` 和 `expectedSavedRevision` 让后端读取，避免传输大型历史快照。

`trip` 必须是项目现有 Trip JSON，不是自然语言介绍。`requirements` 可以覆盖 profile 的 budget、maxSteps、startMinute、endMinute、date、days、mustVisit、avoidPlaceIds、rhythm、energy、transportPreference、companionNeeds 等字段。

预算标准格式为 AUD / 每人每天活动预算。也接受 `activityBudget: {amount:60,currency:"AUD",basis:"perPersonPerDay"}`；其他口径返回 needs_info，不自动换算汇率或人数。

需要跨请求关联时，携带 `X-Planning-Run: UUID`。未携带时服务端创建编号并返回响应头。

### 版本规则

- 地点、时间、交通或草案内容变化：增加 planRevision。
- 用户已确认需求变化：增加 requirementsRevision。
- 相同版本但内容不同：409。
- 新版本注册后，旧版本检查不能授权保存，即使内容没有改变。

### 返回

包含 reviewId、runId、planId、planRevision、requirementsRevision、decision、canConfirm、modelReviewCompleted、summary、issues、toolsUsed、observations、checkedAt、validUntil。

所有展示文案为英文；每个 issue 包含英文 message、actionLabel，以及稳定的 code、dayIndex、severity、targetAgent、suggestedAction；适用时包含 actual 和 limit。

- revise：交给 itinerary_planner，按 issues 修改，增加版本，再次提交检查。
- needs_info：看 handoff.targetAgent；日期/预算歧义交给 requirements_agent，缺少路线/地点证据交给 itinerary_planner 补充工具结果。
- unavailable：保留草案并重试，不声称通过。
- pass：可以调用新确认接口，仍需提醒未核实的价格/营业等 warning。

编排层负责把 handoff 真正交给队友的 Agent。建议最多两轮自动返工；检查模块本身不自动重写草案。

## 工具与证据

- constraint_check：费用、步数、时间连续性与返程、日期、重复、必去/避开、交通偏好、日程快照一致性。无障碍/过敏需求无法核实会返回 needs_info。
- route_evidence：复用现有 WalkingEvidence，核对有方向的 walkingRoutes、时间戳和行程路段。Apple MapKit 仍由 iOS 获取；这是 client_reported，非服务端独立认证。仅写 source="Apple Maps" 不算证据。缺失/过期不得用直线估算冒充。
- place_evidence：先查当前数据源的 PlaceCatalog；缺少地点时查询 PoiService，精确比对 ID 与坐标，最多八次查询。所选 Zetaris 来源继续生效。
- refresh_transit：复用 TripTransportRouter，按该日和出发时间核对已规划的公共交通，最多八段。TfNSW 与 Zetaris 时刻表的来源属性不变，不把静态数据称为实时班次。

价格、步数与场内活动仍可能是估算；pass 表示当前检查范围内通过，不代表实际花费、无障碍或道路永久保证。

## 读取：GET /agents/feasibility/reviews/{reviewId}

返回检查报告，不返回内部保存的原始 trip 和 requirements。

## 确认：POST /agents/feasibility/reviews/{reviewId}/confirm

```json
{"planRevision":1,"requirementsRevision":1,"expectedSavedRevision":0}
```

- 新行程 expectedSavedRevision 为 0；修改已有行程使用其数据库 revision。
- 保存的是服务端检查过的快照，不接受替换 trip 或客户端 canConfirm。
- 检查未通过、过期或版本变化不能确认。过期时间最多十分钟，并受最早的关键证据有效期限制。
- 重复提交同一个确认返回同一次结果，不额外创建行程。
- 数据库乐观锁继续保护已有行程。

**重要：现有 /trips 兼容保存接口尚未全局替换。队友的新流程必须调用这里的确认接口；当前不能声称旧 iOS 所有保存入口都已强制经过此 Agent。**

## 模型与运行

复用 SecretStore / ModelProviderSettings：已有 NVIDIA 直连配置可以运行；Nightshift 通过 AI_PROVIDER、NIGHTSHIFT_BASE_URL、NIGHTSHIFT_API_KEY、NIGHTSHIFT_MODEL 选择。选择 Nightshift 后不静默回退 NIM。不能把代码适配完成等同于 Nightshift 服务已经启用或满足全部赛事要求。

检查器有独立 HTTP 模型入口，不是把规划 Agent 改名。规则工具明确标记为 deterministic；模型工具选择和最终决策保存到统一运行日志。

私密快照位于 SYDNEYBUDDY_DATA/.feasibility-reviews/，日志位于 .planning-runs/，目录/文件分别使用 0700/0600（Unix），均被 Git 忽略。

## Windows / 无 iOS 联调

需要 .NET 10、Node 24 和按项目文档启动的 PostgreSQL。启动 API 后，使用 Python 3 标准库脚本：

```sh
python scripts/check_feasibility.py --draft your-draft.json --plan-id team-draft-001 --plan-revision 1 --requirements-revision 1
```

云端数据加 `--data-source zetaris-cloud`。脚本会剔除与本次检查无关的 previousAdjustment，避免历史快照导致请求过大。默认只检查，不确认保存。Apple 路段由 iOS 端提供；自动化测试使用明确的测试证据和模型替身，不能把替身结果当成真实模型证明。

当前检查快照与确认互斥按单后端进程实现；部署多实例时，应将检查版本和确认回执迁移到 PostgreSQL 并使用事务锁。
