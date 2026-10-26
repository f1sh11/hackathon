# Koko 对话与记忆接口测试

基线：main `7b726a1b84af5fd3e2c8bb7eca83b85f9cae4a79`。这是后端接口审计工具，复用 App 的真实路由、提取/校验、PostgreSQL 和 H-MEM。生产对话、模型提示和 UI 均没有修改。

## 已记录的结果

2026-10-26 悉尼时间，固定桩模式实际 HTTP 执行 **19 案例：16 PASS / 3 FAIL / 0 NOT_RUN**。三个失败是当前 App 行为，不是用假通过替代的问题：

1. PATCH 从 CNY 改成 AUD 时，复用了原金额 `100`，甚至变为资料齐全。
2. “这次不要坐公交，想步行”被解析为 `transportPreference=balanced`。
3. “每人每天100澳元活动预算，不是全程”被解析为 `budgetBasis=perPersonTrip`。

逐项请求、响应、复现及 iOS 交接在 [ISSUES.md](ISSUES.md)。复现样本在 [evidence/main-7b726a1](evidence/main-7b726a1/README.md)；ZIP 中有完整 HTML、CSV 和 JSON。样本仅包含合成测试资料。

真实模型未配置的独立环境另记录了 **3 PASS / 1 FAIL / 15 NOT_RUN**：四个不依赖模型的接口案例实际执行，其余跳过，**NVIDIA 调用数为 0**。这不是一次真实模型端到端验收。配置好专用 key 后可重复同一套输入，结果单独报告。

## 环境

- Python 3.10+（建议 3.12），工具代码仅用标准库。Windows 若没有 IANA 时区数据，先执行 `py -m pip install tzdata`；缺失数据时会在批测写入前明确报错。
- .NET 10 SDK，复用 API 项目的依赖。
- Node 24+，供仓库现有 H-MEM bridge 使用。
- 一个专用 PostgreSQL 测试数据库。不要填 App 正在使用的数据库连接。

FixtureHost 只监听 `127.0.0.1:8897`，给每个环境创建随机 `koko_cli_<guid>` schema 和带所有权标记的数据目录。CLI 会先检查 `/__koko_test__/metadata`；普通 App 地址、云端地址、没有隔离证明的服务都会被拒绝，尚未发送任何业务写入。

例如先启动一个独立容器（示例账号和密码仅用于这个测试库）：

```bash
docker run --rm --name koko-api-test-db \
  -e POSTGRES_DB=koko_api_test -e POSTGRES_USER=koko_audit \
  -e POSTGRES_PASSWORD=koko_audit_dev \
  -p 127.0.0.1:55440:5432 postgres:17
```

另开终端，在仓库根目录配置连接并运行。macOS/Linux：

```bash
export KOKO_TEST_CONNECTION='Host=127.0.0.1;Port=55440;Database=koko_api_test;Username=koko_audit;Password=koko_audit_dev'
python3 tools/koko_api/run_suite.py --mode fixture --out artifacts/koko-api/fixture
```

Windows PowerShell：

```powershell
$env:KOKO_TEST_CONNECTION = 'Host=127.0.0.1;Port=55440;Database=koko_api_test;Username=koko_audit;Password=koko_audit_dev'
py tools/koko_api/run_suite.py --mode fixture --out artifacts/koko-api/fixture
```

这一条命令编译测试宿主、启动真实 API、执行案例、逐案保存报告，并停止/清理本次宿主创建的 schema 和目录。再次运行同一输出目录会重新生成报告，不复用上次已销毁环境的 journal。已有端口被占用时不会接管或终止那个进程。

最新 App 接口会在会话创建、资料修改和 turn 中携带 `language`。工具默认按本任务的简体中文输入发送 `zh-Hans`；CLI 与启动器都支持 `--language zh-Hant` 或 `--language en`。请求语言随重试正文一起持久化，切换语言不会改写已保存请求。固定桩文案仍是预置文本，不据此判定三语生成质量。

本地交付环境没有 Docker/native PostgreSQL，使用私有 **PGlite 0.5.11 / PostgreSQL 18.6** socket 串行执行，报告记录实际数据库引擎。仓库新增的 [Koko audit CI](../../.github/workflows/koko-api-audit.yml) 使用独立 **原生 PostgreSQL 17**。PGlite 结果不证明原生数据库的并发/事务行为。

## 批量运行与输出

```bash
# 单个复现；仍启动独立宿主并调用真实 API。
python3 tools/koko_api/run_suite.py --mode fixture \
  --case 13-probe-patch-currency --out artifacts/koko-api/repro13

# 宿主已经启动时，直接运行客户端。
python3 tools/koko_api_cli.py --state artifacts/koko-api/batch-state.json \
  batch --mode fixture --out artifacts/koko-api/batch

python3 -m unittest discover -s tools/koko_api/tests -p 'test_*.py'
```

退出码：`0` 全部通过；`1` 至少一个实际案例失败（报告仍生成）；`2` 工具/配置错误；`3` 没有失败但有未运行案例。不要把退出码 `1` 改成“全部通过”。CI 的 audit job 校验工具能完整记录结果，把 App 失败作为 warning 和报告保留；job 成功不表示 App 的红色案例通过。

每次执行产生：

| 文件 | 内容 |
|---|---|
| `report.html` | 静态报告：每轮输入、Koko 回复、字段变化、追问/选项、缺项、候选偏好、记忆变化和请求/响应 |
| `summary.csv` | 每个案例的 PASS / FAIL / NOT_RUN、原因、模型配置/调用次数和人工评阅项 |
| `turns.csv` | 每轮结构化输出、记忆前后值、断言和原始请求/响应 |
| `run.json` | 完整脱敏证据、时钟、源提交、案例/工具/提示哈希、模型 transport 记录 |

HTML 转义不可信文本；CSV 防止公式注入。凭据、Authorization、连接字符串及已配置 key 不写入报告。不要使用真实个人资料编写案例。

`sourceCommit` 是实际运行 checkout；`caseBaselineCommit` 是这套案例最初审计的 main。报告也记录生产目录是否有未提交修改，不能用旧常量冒充新的运行版本。

## 自由多轮对话与恢复

让宿主在终端 A 持续运行；`KOKO_TEST_DATA` 是本工具创建的独立目录，默认保留以便重启恢复：

```bash
export KOKO_TEST_DATA="$PWD/artifacts/koko-api/interactive-host"
dotnet run --project tools/koko_api/FixtureHost/KokoApiFixtureHost.csproj -c Release
```

终端 B 使用同一个 state 文件。以下命令均复用现有 App API；不生成工具自己的聊天回复：

```bash
python3 tools/koko_api_cli.py --state .koko-api-state.json create
python3 tools/koko_api_cli.py --state .koko-api-state.json show
python3 tools/koko_api_cli.py --state .koko-api-state.json send '去悉尼玩三天'
python3 tools/koko_api_cli.py --state .koko-api-state.json choose --index 1
python3 tools/koko_api_cli.py --state .koko-api-state.json patch '{"days":"2"}'
python3 tools/koko_api_cli.py --state .koko-api-state.json memories
python3 tools/koko_api_cli.py --state .koko-api-state.json memory normal_start_time remember --value late
python3 tools/koko_api_cli.py --state .koko-api-state.json memory queue_tolerance_minutes trip-only
python3 tools/koko_api_cli.py --state .koko-api-state.json memory interests ignore
python3 tools/koko_api_cli.py --state .koko-api-state.json resume SESSION_ID
python3 tools/koko_api_cli.py --state .koko-api-state.json retry
python3 tools/koko_api_cli.py --state .koko-api-state.json chat
```

固定桩宿主只响应已加载的精确消息脚本。要在 fixture 交互模式发送任意文本，先用 `POST /__koko_test__/fixture` 加载自己的预置 JSON；没有脚本的消息明确失败，不会临时编造一段回复。真正自由聊天使用下面的 live 环境。`--json` 放在子命令之前可查看完整响应。

`chat` 支持 `/pick N`、`/show`、`/retry`、`/abandon`、`/memories`、`/quit`。当前 App 的 `compose_*`、`details*`、`keep_draft` 是本地输入/编辑行为，CLI 不把它们伪造成服务器 turn；使用自由文本或 `patch` 对应字段。选项编号从 1 开始，提交时携带最新 assistant 的 questionId、optionId 和完整原文。

发送前 journal 先保存 UUID 和原请求。断流或超时后只能重试原请求，或显式 `abandon` 将其归档，再输入新内容；不会被下一条输入悄悄覆盖。`abandon` 不撤销已到达后端的操作。恢复旧会话失败时不会把它的请求发到另一会话。服务端只保留最近 100 个请求 ID 的幂等记录，不保证无限历史重放。

`cleanup` 仅删除 state ledger 中记录的测试 session/memory ID，失败返回非零。它不会清空记忆表。要清理整个交互宿主，使用同一 `KOKO_TEST_DATA` 启动并设置 `KOKO_TEST_CLEANUP_ON_EXIT=1`，再正常停止；宿主只删除所有权标记和 schema 名称一致的测试环境。

## 真实模型抽查

FixtureHost 从不导入团队的 `.secrets.json`，live 必须使用专用测试 key：

```bash
# 先通过安全方式注入环境变量 KOKO_TEST_NVIDIA_API_KEY。
# KOKO_TEST_MODEL 可选；未设置时沿用仓库 SecretStore.DefaultModel。
python3 tools/koko_api/run_suite.py --mode live --out artifacts/koko-api/live
```

不要把 key 写入命令历史、案例 JSON、报告或仓库；实际使用时通过安全环境变量设置。缺少 key 时需要模型的案例记为 NOT_RUN，不用桩回复补上。故障注入案例只在 fixture 模式运行。要启动可恢复的 live 交互宿主，还需 `KOKO_TEST_MODE=live` 和 `KOKO_TEST_LIVE_OPT_IN=1`。

本轮 fixture 的模型超时是明确标记的 `synthetic_timeout_exception`：验证实际 PetModel/接口的超时异常恢复路径，不声称等待过真实 NVIDIA 的 100 秒期限。无效 JSON 和上游 HTTP 错误同样单独记录故障来源。

## 判读边界和责任

- 当前响应没有 `canGenerate`。报告的 `derivedReady` 仅由真实响应 `missingImportantFields=[]`、`nextField=""`、`completed=total=7` 推导，表示后端资料完整性；不是规划算法全面校验，也不证明手机生成按钮可点击。
- 结构化字段、同意/拒绝、持久记忆变化和请求幂等性自动断言。亲切程度、追问是否自然、重复问题是否打扰，保留为 `humanReview`，不以关键词自动打分。
- 案例 09 证明真实 H-MEM 写入/数据库持久化及新会话的结构化偏好复用；不宣称验证了 H-MEM 语义召回质量，或真实模型如何使用这些记忆。
- iOS 同学用报告中的输入顺序验收：键盘遮挡、选项/输入框联动、生成按钮、动画、加载和跳转。本工具不对这些页面项目给 PASS。

## 工程交接

| 文件 | 职责 |
|---|---|
| `../koko_api_cli.py` | 人工 CLI 和 chat 入口 |
| `client.py` | 隔离证明、JSON/NDJSON transport、请求 journal、会话恢复、所属数据清理 |
| `runner.py` | 按输入顺序执行案例、真实状态快照、自动断言、模型/版本证据 |
| `cases.json` | 19 个可重复输入、预置模型输出、预期结构化状态、人工评阅项 |
| `report.py` | CSV/HTML/JSON 序列化、转义和脱敏 |
| `FixtureHost/` | 测试宿主；真实 App 服务/路由，只替换 NVIDIA transport |
| `tests/` | 工具自身的协议、安全、恢复和报告测试 |

数据流：CLI → App 的实际 `/pet` 和 `/memories` 路由 → 实际 PetModel/提取与记忆服务 → 独立 PostgreSQL/H-MEM → NDJSON 完成状态 → 自动断言与报告。固定桩只提供外部模型边界的预置 SSE，不决定 App 的字段处理或下一步。

建议先读 `client.py` 的 `parse_ndjson` / `TestSession.turn`（完成与重试边界）、`runner.py` 的 `run_case`（状态和断言）、`FixtureHost/FixtureTransport.cs`（桩与真实模型隔离）、`ISSUES.md`（下一轮修复入口）。添加场景只需复制一项案例并修改输入、fixture 和结构化断言，不需要另写聊天逻辑。

先排错：连不上看 metadata/端口/专用库；结果 NOT_RUN 看模型配置和原因；选项失败先 `show` 读取当前 questionId；记忆异常对照真实 `/memories` 前后值与 `memoryWarning`；模型行为看 `modelTrace`，不要把预置回复当成模型质量证据。
