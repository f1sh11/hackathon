# 无 iOS 的命令行演示

入口是 `scripts/demo_agent.py`，只使用 Python 3.9+ 标准库。无需安装 iOS、Xcode 或第三方 Python 包。默认不调用网络、不使用密钥、不保存行程；不会执行 `/trips` 或检查报告的 `/confirm`。

## 先运行三个离线案例

在仓库根目录运行，Windows 可将 `python3` 换成 `python`：

```sh
python3 scripts/demo_agent.py --input input_examples/headless/ordinary-day.json
python3 scripts/demo_agent.py --input input_examples/headless/low-budget.json
python3 scripts/demo_agent.py --input input_examples/headless/route-unavailable.json
```

| 输入 | 离线预期结果 | 具体行为 |
| --- | --- | --- |
| `ordinary-day.json` | `pass`，返工一次 | 初稿含可选付费地点，规则检查发现超出 A$20 预算；移除可选地点后重新计算通过 |
| `low-budget.json` | `blocked` | 三个地点均为必去，样例费用超过 A$5；保留必去与预算，解释需要用户修改需求 |
| `route-unavailable.json` | `needs_info` | 模拟路线缺失，路段为空、到达时间和步数未知，不生成假路线或批准结果 |

日期默认取悉尼明天，可用 `--date 2026-10-28` 指定。部分 Windows Python 环境没有 IANA 时区资料，此时请显式传 `--date`，不需要为演练安装额外依赖。修改输入中的 `profile.budget`、`mustVisit`、时间窗口和步数后，结果会重新计算；脚本没有读取预先录好的最终答案。

**离线模式是流程演练，不是生产 Agent 系统。** 它使用独立的简单 Python 选点策略与规则检查器，地点价格与每段 15 分钟 / 600 米都是明确的合成输入，不调用 C# 后端、LLM、Zetaris、Meterless 或地图。离线 `pass` 只表示这些样例规则通过；`canConfirm` 和 `modelReviewCompleted` 始终为 false。不得把其日志用作真实模型或赞助商接入证明。离线演练仅支持一天。

## 真实后端联调

先按仓库 README 启动 PostgreSQL、Node 24 和当前目录的 .NET API，并在后端配置规划模型和独立审核模型。`/health` 应包含 `planningAgentConfigured`；旧服务只有 `status=ok` 不够。脚本不会自动启动或替换现有服务，也不会开启付费开关。

```sh
python3 scripts/demo_agent.py --mode live --input input_examples/headless/ordinary-day.json
```

这里 `live` 表示真的向 API 发请求，**不等于所有旅游资料都是实时数据**。默认 `--data-mode demo` 仍是后端的示例资料模式，但 `useAgent=true` 会请求真实规划模型。后端原有的 `assisted` / `live` 模式可通过 `--data-mode` 选择，继续受原有配置和付费许可约束。

Zetaris 联调可加：

```sh
python3 scripts/demo_agent.py --mode live --data-source zetaris-cloud --input input_examples/headless/ordinary-day.json
```

不同资料源的 ID、成本和模型选择可能不同，三个案例的离线预期不能当作真实服务的固定答案。`preferredIds` 与 `sampleRouteFailure` 只用于离线演练；真实模式只提交 profile，不通过样例开关伪造故障。

真实编排：

1. `GET /health` 检查当前后端版本和模型配置。
2. `POST /plan` 发送原始需求，强制请求规划 Agent；取得未保存草案。
3. `POST /agents/feasibility/reviews` 让独立检查 Agent 审核，保留原始需求作为约束。
4. 收到 `revise` 且 handoff 目标为 `itinerary_planner` 时，将问题代码和数值附在下一轮 request 中，重新生成。预算、必去、日期、时间和步数不放宽；planRevision 递增，requirementsRevision 保持 1。
5. 最多重做两轮；`needs_info` / `unavailable` 立即停止，`pass` 还必须同时有 `canConfirm=true` 和 `modelReviewCompleted=true`。成功也只输出草案，不替用户保存。
6. 用同一个 `X-Planning-Run` 导出 `/planning/runs/{run_id}` 的真实后端日志。CLI 自己发出的“请求已发送”日志不作为模型已执行的证据。

### 当前路线证据的实际限制

现有独立检查器主要接受方向、距离、耗时和时间戳匹配的 MapKit 步行观察。默认 `/plan` 的示例距离不是这类证据，Google live 路线也不能自动视为 MapKit 观察。因此，**真实模式目前可能返回 `needs_info`，不能承诺无 iOS 环境下一定完成真实审核闭环**。

若已经拥有真实、仍在六小时有效期内、覆盖候选路段的 MapKit 观察，可传入 JSON 数组：

```sh
python3 scripts/demo_agent.py --mode live --input input_examples/headless/ordinary-day.json --walking-evidence /absolute/path/to/current-mapkit-observations.json
```

该文件不是必需的新模板；每项沿用现有 `walkingRoutes` 的 `from` / `to` / `meters` / `seconds` / `fetchedAt` 契约。不能生成随机距离、刷新旧时间戳、把 Valhalla 结果标成 Apple 来源来绕过检查。此参数不能和 Google `--data-mode live` 混用。

要让真实正常案例在完全不依赖 MapKit 的环境下通过，后续应扩展**后端**路线证据工具，让它自己请求已配置的路线服务、保留真实来源并重新核对日程。当前脚本明确暴露这个缺口，没有伪造证据。后端模型未配置、上游超时或 API 返回 422 也会如实记录，不回退到离线成功答案；422 不自动解释成“预算冲突”。

## 运行文件与退出码

每次创建新的 `.demo-runs/<run_id>/`，也可用 `--output-dir <不存在的新目录>` 指定。已有结果不覆盖。文件可能包含用户需求和草案，默认目录已被 Git 忽略。

- `input.json`：本次输入，含解析后的实际日期。
- `draft-1.json`、`review-1.json` 等：逐轮草案与检查结果。
- `events.jsonl`：实时打印到终端的客户端 / 离线事件，标注模式、运行编号和返工次数。
- `backend-events.json`：仅在真实后端日志导出成功时存在，保留后端实际角色、规则 / 模型区分和调用结果。
- `result.json`：最终状态、模式、资料来源、未保存声明；失败时说明失败阶段。

退出码：`0` 表示本次模式的检查通过，`2` 表示冲突、待补证据或规划被拒绝，`1` 表示配置、通信或输入错误。后两个案例故意不返回 0，不能据此称脚本出错。

远端后端需 HTTPS。可选访问码从 `SYDNEYBUDDY_DEMO_TOKEN` 环境变量读取，只通过 HTTPS 发送，不作为命令行参数或输出。客户端拒绝重定向，不在错误日志里复制服务响应正文。

## 保存提交示例

脚本不会创建可提交的预录输出。真实调用完成后，先核查 `result.json` 和 `backend-events.json`，删去个人信息再把需要展示的结果人工选入 `output_examples/`。同时记录日期、服务模式、模型和数据来源；失败结果也可作为失败处理证据。不要把离线 `pass` 改名为真实 Agent 成功。

验证脚本：

```sh
python3 -m unittest discover -s scripts/tests -p test_headless_demo.py
```

这些测试使用离线策略和 HTTP 传输替身，验证约束不被放宽、返工有上限、缺证据阻止通过及真实模式不回退样例；不证明真实 NVIDIA / Meterless / Zetaris 已连通。
