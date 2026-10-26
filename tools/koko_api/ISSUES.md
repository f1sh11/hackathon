# Koko 对话与记忆接口问题清单

本轮在 `main` 提交 `7b726a1b84af5fd3e2c8bb7eca83b85f9cae4a79` 上运行实际 HTTP 接口，共 **19 个案例：16 PASS、3 FAIL、0 NOT_RUN**。这里的三个 FAIL 是保留正确预期后捕获的业务问题，没有为得到全绿而修改断言。

## 证据和结论边界

- 执行模式：`fixture`，模型标识 `koko/scripted-sse-fixture`。脚本只替换 NVIDIA HTTP 响应；生产 `PetModel` 的 SSE/JSON 解析、路由、字段处理和记忆流程仍实际执行。
- 测试开始：UTC `2026-10-26T03:57:18.461838+00:00`；对应悉尼时区 `Australia/Sydney`。环境时钟记录为悉尼 `2026-10-26T14:57:18.473244+11:00`。
- 来源、模型、提示哈希、每轮请求/响应和断言保存在 [完整脱敏证据包](evidence/main-7b726a1/api-reports.zip)。解压后查看 `fixture/run.json`、`fixture/report.html`、`fixture/summary.csv`、`fixture/turns.csv`。也可直接查看 [案例汇总](evidence/main-7b726a1/fixture/summary.csv) 和 [问题证据摘录](evidence/main-7b726a1/fixture/findings.json)。
- 当前执行环境没有配置真实 NVIDIA 模型。本清单不对真实模型的理解能力、亲切程度或真实端到端对话质量作通过结论。fixture 的 `modelConfigured:true` 只表示脚本传输已就绪，不代表真实 NVIDIA 已连接。
- API 没有名为 `canGenerate` 的响应字段。下文 `derivedReady` 是工具根据 `missingImportantFields=[]`、`nextField=""`、`completed==total` 计算的**后端字段完整度**，不是手机按钮出现、可点击或生成成功的证明。
- 每个案例仅使用工具创建的隔离会话与测试记忆；捕获的 ID 和话语均为合成测试数据。没有操作既有用户数据。本轮只交付工具、证据和问题报告，未修生产逻辑或 UI。

## 单例复现方式

先按 [README](README.md) 配置 .NET 10、Node.js 24+ 和 `KOKO_TEST_CONNECTION` 指向的专用测试数据库。以下命令从仓库根目录运行；启动器会创建隔离 Host 并调用同一批量执行器：

```bash
python3 tools/koko_api/run_suite.py --mode fixture --case 13-probe-patch-currency --out artifacts/koko-api/repro13
python3 tools/koko_api/run_suite.py --mode fixture --case 14-probe-negated-transport --out artifacts/koko-api/repro14
python3 tools/koko_api/run_suite.py --mode fixture --case 15-probe-negated-budget-basis --out artifacts/koko-api/repro15
```

如果已有**独占使用的隔离测试 Host** 运行在 `127.0.0.1:8897`，也可直接使用 CLI 的 `batch --case`。不要将这些写入案例指向已部署或共享的用户服务：

```bash
python3 tools/koko_api_cli.py --url http://127.0.0.1:8897 --state artifacts/koko-api/repro13-state.json batch --mode fixture --case 13-probe-patch-currency --out artifacts/koko-api/repro13
python3 tools/koko_api_cli.py --url http://127.0.0.1:8897 --state artifacts/koko-api/repro14-state.json batch --mode fixture --case 14-probe-negated-transport --out artifacts/koko-api/repro14
python3 tools/koko_api_cli.py --url http://127.0.0.1:8897 --state artifacts/koko-api/repro15-state.json batch --mode fixture --case 15-probe-negated-budget-basis --out artifacts/koko-api/repro15
```

三个命令在本基线预期返回退出码 `1`，因为发现业务断言失败；这与启动失败或工具错误不同。每次重跑会创建新 ID，并根据测试时钟生成未来日期。下列 ID、UUID 和日期是已捕获运行的原始摘录，不应用作重跑时的固定输入。

## 1. PATCH 修改币种后沿用旧币种金额，字段完整度错误变为可规划

**案例：`13-probe-patch-currency`，步骤 3，FAIL。优先级：高，预算值可能被重新解释。**

复现顺序：创建三天、两人的悉尼旅行，预算为全体全程 `100 CNY`；读取会话确认尚缺可支持币种；只 PATCH 币种为 `AUD`，未提交新金额。

创建请求中的资料摘录：

```json
{
  "profilingVersion": 2,
  "companionVersion": 1,
  "hasMetKoko": true,
  "nickname": "",
  "fields": {
    "destination": "悉尼",
    "date": "2026-11-25",
    "days": "3",
    "travelers": "2",
    "budget": "100",
    "budgetCurrency": "CNY",
    "budgetBasis": "groupTrip",
    "budgetCoverage": "activities"
  }
}
```

实际修改请求：

```http
PATCH /pet/sessions/2045ac78e0254374adb1dd65c01cedf8
Content-Type: application/json

{"fields":{"budgetCurrency":"AUD"},"language":"zh-Hans"}
```

HTTP `200` 响应摘录：

```json
{
  "fields": {"budget":"100","budgetCurrency":"AUD","budgetBasis":"groupTrip"},
  "nextField": "",
  "missingImportantFields": [],
  "completed": 7,
  "total": 7
}
```

Koko 实际回复末尾为：“资料准备齐啦！可以直接生成行程，也可以继续聊或单独修改。”

| 检查项 | 预期 | 实际 |
|---|---|---|
| `session.fields.budgetCurrency` | `AUD` | `AUD` |
| `session.fields.budget` | 不存在，等待用户确认新币种金额 | 仍为 `"100"`，断言失败 |
| 工具 `derivedReady` | `false` | `true`，断言失败 |

原始 `100 CNY` 因而被无提示地当作 `100 AUD` 使用。这不是汇率转换；请求中根本没有提供新金额。本案例没有调用模型，模型调用轨迹为 `totalCalls:0`。

相关模块：[PetEndpoints.cs:24](../../server-dotnet/SydneyBuddy.Api/Api/PetEndpoints.cs#L24) 的 PATCH 直接调用 `PetQuestions.Apply`；与同文件 [Turn 的币种变化保护:129](../../server-dotnet/SydneyBuddy.Api/Api/PetEndpoints.cs#L129) 行为不一致。优先检查两个写入入口对币种与金额关联的处理，保留现有正确的 `/turn` 回归检查。

**iOS 待人工验收，未在本轮执行：** 在隔离后端建立上述 CNY 草稿，用旅行资料编辑入口仅改变币种，检查客户端实际发送的 PATCH 是否也包含旧金额、摘要是否要求重新输入金额、生成按钮是否仍可用。若 App 一次提交币种和金额，应另行确认这是用户明确重新输入，还是表单沿用了旧值。接口结果不能替代该页面结论。

## 2. “不要坐公交”被保存为允许公共交通，回复与字段不一致

**案例：`14-probe-negated-transport`，步骤 2，FAIL。优先级：中，明确的本次交通要求被反向记录。**

复现顺序：建立必要资料已齐的三天、两人悉尼会话，活动预算每人每天 `120 AUD`；发送下面的自由文本。脚本模型返回 `fields:{}`、`preferences:[]`，让生产显式短语解析器独立处理交通要求。

实际请求：

```http
POST /pet/sessions/285b16c197b34c73bb35c953080a640d/turn
Content-Type: application/json

{"language":"zh-Hans","message":"这次不要坐公交，想步行","requestId":"35593ccf-847f-40d7-b695-89783406bf0c"}
```

HTTP `200`，NDJSON 正常以 `done` 结束；最终 `session` 摘录：

```json
{
  "fields": {"transportPreference":"balanced"},
  "planningFields": {"transportPreference":"balanced"},
  "nextField": "",
  "missingImportantFields": []
}
```

实际 assistant 回复却是：“这次先按你想步行的方式准备。\n资料准备齐啦！可以直接生成行程，也可以继续聊或单独修改。”

| 检查项 | 预期 | 实际 |
|---|---|---|
| `session.fields.transportPreference` | `walk` | `balanced`，断言失败 |
| `memoryDelta` | `0` | `0`，通过 |

`balanced` 在 App 中代表步行加公共交通。这个请求完成了消息保存，也没有意外写入长期记忆，但本次交通约束与原话相反。脚本回复只是固定输入；本结论针对生产解析器和持久化状态，不表示真实 NVIDIA 一定会给出同样措辞。

相关模块：[PetConversation.cs:249](../../server-dotnet/SydneyBuddy.Api/Services/PetConversation.cs#L249) 的 `PetExplicitFacts.Apply` 先识别若干步行表达，再在 [251 行](../../server-dotnet/SydneyBuddy.Api/Services/PetConversation.cs#L251) 仅凭“公交”等词写入 `balanced`，没有识别本次“不要坐公交”的否定。App 后续映射见 [PetChat.swift:922](../../ios/SydneyBuddy/PetChat.swift#L922)。

**iOS 待人工验收，未在本轮执行：** 在隔离会话输入同一句话，查看本次需求摘要及生成请求中的 `transportPreference`，核对回复与摘要是否一致；如果继续生成，再检查实际交通安排和用户可纠正入口。不能仅看到“想步行”的回复就判定页面或规划正确。

## 3. “不是全程”覆盖了已经明确的“每人每天”预算范围

**案例：`15-probe-negated-budget-basis`，步骤 2，FAIL。优先级：高，预算口径与每日金额被改变。**

复现顺序：建立必要资料已齐的三天、两人悉尼会话，原活动预算每人每天 `120 AUD`；发送下面的明确修正。脚本模型返回 `fields:{}`，没有提供一个错误的 `budgetBasis`。

实际请求：

```http
POST /pet/sessions/cd0b1a90afd64578b073f7a135ead4e2/turn
Content-Type: application/json

{"language":"zh-Hans","message":"每人每天100澳元活动预算，不是全程","requestId":"e07bc0ea-3b94-49c5-bd12-cd0f24d0e705"}
```

HTTP `200`，NDJSON 正常以 `done` 结束；最终 `session` 摘录：

```json
{
  "fields": {
    "budget": "100",
    "budgetCurrency": "AUD",
    "budgetBasis": "perPersonTrip",
    "budgetCoverage": "activities",
    "days": "3",
    "travelers": "2"
  },
  "nextField": "",
  "missingImportantFields": []
}
```

实际 assistant 回复：“收到！预算 100 AUD（每人全程），先收进这次的小背包。换算为每人每日活动预算约 33.33 AUD。”随后仍提示资料准备齐。

| 检查项 | 预期 | 实际 |
|---|---|---|
| `session.fields.budget` | `"100"` | `"100"` |
| `session.fields.budgetCurrency` | `AUD` | `AUD` |
| `session.fields.budgetBasis` | `perPersonPerDay` | `perPersonTrip`，断言失败 |
| `session.fields.budgetCoverage` | `activities` | `activities` |

三天行程中，明确的每人每天 `100 AUD` 被解释为每人全程 `100 AUD`，回复中的每日预算相应降为 `33.33 AUD`。这不是尚未澄清的口径，原话已经明确“每天”并否定“全程”。

相关模块：[PetConversation.cs:274](../../server-dotnet/SydneyBuddy.Api/Services/PetConversation.cs#L274) 已先识别 `perPersonPerDay`；随后 [277 行](../../server-dotnet/SydneyBuddy.Api/Services/PetConversation.cs#L277) 对整句执行 `Contains("全程")`，并在 278 行覆盖为 `perPersonTrip`，没有区分否定语境。应重点审查这个生产显式解析分支，不应通过改变脚本回复掩盖字段错误。

**iOS 待人工验收，未在本轮执行：** 输入该修正后检查预算范围、原始金额和每日换算摘要；再查看生成请求是否保持每人每天 `100 AUD`。输入、摘要、按钮状态和最终行程费用展示均需手机侧确认，不能由本次接口测试代判通过。

## 独立的人工自然度评阅备注

`01-information-reuse` 的自动状态为 **PASS**：目的地为悉尼、天数为 `3`，`nextField` 为 `date`，后续 GET 也保留这两项。可是实际最终回复又追加“接下来选出发日期和天数，最多 15 天”，按钮文字也是“选日期和天数”。“三天”已经明确，再同时索要“天数”可能仍有重复问卷感。

这是人工体验评阅点，**没有通过关键词自动判为第四个 FAIL**，也不用于证明真实模型语气优劣。当前基线的 `KokoDialogue.StepQuestion` 将日期与天数合并成统一时间步骤，相关位置为 [ProfilingDialogue.cs:192](../../server-dotnet/SydneyBuddy.Api/Services/ProfilingDialogue.cs#L192)。建议 iOS 同学结合时间编辑页实际预填值，人工判断用户是否被迫重填已知天数；真实模型自然度还应另跑 `live` 抽查。
