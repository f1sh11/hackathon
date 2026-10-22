# Sydney Buddy · 悉尼旅行 Agent

原生 **SwiftUI iOS + Python API** 旅行规划 MVP：单题引导收集偏好，NVIDIA Nemotron 推荐候选地点，程序检查路线约束，再由用户确认保存。地图、地点、路线与天气使用 Google Maps Platform，授权偏好使用官方 Meterless H-MEM 引擎。

本版本替换仓库原 React/.NET 原型；旧版本保留在 Git 历史中。**当前为本机单用户原型，未部署线上服务，也未提交 HackOS。**

![游戏化引导](docs/buddy-onboarding.png)

[地图与出发任务截图](docs/buddy-preparation.png) · [Google 天气截图](docs/app-google-weather.png) · [演示录像 MP4](docs/app-demo.mp4) · [验证记录](docs/verification.md)

## 已实现

- 8步轻量引导：日期、天数、节奏、兴趣、预算、起点、时间、确认。Buddy 即时回应，带进度条与按压反馈。
- 悉尼市区1–3天步行规划，8个限定候选地标，每天最多4站，跨天不重复。
- Google 地图标记和真实步行路线；Places 查询坐标与常规营业时段；Weather 获取未来10天逐日预报。
- Nemotron 只对给定地点ID排序，程序独立检查时间、步数、活动预算、休息、衔接和返程。
- AI调整先生成草案，展示地点与步数变化，确认后才替换保存；版本冲突返回409。
- SQLite 多行程管理；iOS 本机均分记账以整数分保存。
- 同意后保存偏好并通过 H-MEM 检索，支持撤回；追加审计账本仍保留历史记录。
- 出发准备清单：查看路线、成功刷新天气、手动确认行李。进度按行程保存在本机，全部完成显示徽章，不设断签惩罚。

## 快速运行

需要 macOS、Xcode（iOS17+ SDK/模拟器）、Python3.9+（本次验证3.14）、Node.js24。Python 服务只用标准库，无需 pip 安装。首次 Xcode 构建需联网下载 Google Maps SDK11.2.0。

```sh
git clone https://github.com/f1sh11/hackathon.git
cd hackathon
python3 server/app.py
```

也可双击 `scripts/start-server.command`。服务启动在 [http://127.0.0.1:8787](http://127.0.0.1:8787)，然后：

1. 打开 `ios/SydneyBuddy.xcodeproj`，选择已安装的 iPhone 模拟器，运行 `SydneyBuddy` scheme。
2. 不配置密钥也能用演示模式，地图显示明确标注的示意图。
3. 配置在线服务：在同一台电脑打开 [本机密钥设置页](http://127.0.0.1:8787/setup)，填写自己的密钥并保存。
4. Google iOS地图密钥保存后，重新构建App。开启引导页或“我的”里的在线推荐。

命令行构建（先用 `xcrun simctl list devices available` 查询可用模拟器）：

```sh
xcodebuild -project ios/SydneyBuddy.xcodeproj -scheme SydneyBuddy \
  -sdk iphonesimulator -configuration Debug \
  -destination 'generic/platform=iOS Simulator' CODE_SIGNING_ALLOWED=NO build
```

模拟器无需Apple付费会员即可构建运行；真机需自己的开发团队/签名。**App中的127.0.0.1只适用于同机模拟器**。真机/线上部署需配置HTTPS后端地址、身份认证、访问控制和滥用防护，本版尚未实现。

## API 密钥配置

仓库发布完整API接口代码和配置模板，**不包含可用密钥**。本机设置页会把密钥保存到忽略文件 `.secrets.json`（权限0600），iOS地图密钥另写入忽略文件 `ios/Config/Secrets.xcconfig`。

| 配置项 | 用途与限制 |
|---|---|
| `NVIDIA_API_KEY` | 服务端调用 NVIDIA NIM；不得打包进App |
| `NVIDIA_MODEL` | 可选；默认 `nvidia/nemotron-3.5-lightning-30b-a3b`，需账号支持 |
| `GOOGLE_SERVER_KEY` | 仅允许 Places API (New)、Routes API、Weather API，按运行服务的出口IP限制 |
| `GOOGLE_IOS_KEY` | 仅允许 Maps SDK for iOS，应用限制为 `com.sydneybuddy.travel` |
| `NODE_BINARY` | 可选，Node24可执行文件路径；否则从PATH查找 |

模板见 [.secrets.example.json](.secrets.example.json) 和 [Secrets.example.xcconfig](ios/Config/Secrets.example.xcconfig)。使用模板时自行复制为实际文件，填入自己的值。也可通过上述同名环境变量配置服务端；本程序**不会自动加载.env**。单独设置 `GOOGLE_IOS_KEY` 环境变量不会生成App配置，应使用设置页或手动填写xcconfig。

Google项目需要启用计费及上述4项服务。已有密钥的IP限制换网络后可能失效；不要取消所有限制来排错。Weather每月前10,000次免费，之后第一档US$0.15/1,000次；地图、Places和Routes分别计费，以[官方价格](https://developers.google.com/maps/billing-and-pricing/pricing)为准。未配置云端硬性费用上限。

## 模式、天气与数据边界

| 模式 | 模型 | 其他数据 |
|---|---|---|
| `demo` | 本地规则，不调用AI | 示例地点、天气、营业时段与距离估算 |
| `assisted` | 真实Nemotron | 示例地点、天气和路线，不请求Google服务端API |
| `live` | 真实Nemotron | Google Places、Routes、Weather；费用/场内步数/停留时间仍为估算 |

在线开关开启后，有Google服务端密钥进入live，否则assisted。存在密钥不代表服务验证成功；地图SDK与后端服务分别配置。

天气按Australia/Sydney日期匹配，每天分别影响室内/室外筛选。Weather每次请求10天、pageSize=10，8秒超时；失败或范围外明确标为未知，不当作晴天。首页可独立刷新天气；刷新不会重排已保存路线，调整要点“问AI”并确认。雨天快捷按钮是用户情景测试，明确标注非实时。Google天气原始响应不做跨请求缓存。

## 后端接口

默认仅监听127.0.0.1:8787，无账户系统，不可直接暴露公网。JSON请求使用 `Content-Type: application/json`；跨站请求受本机来源检查限制。

| 方法/路径 | 说明 |
|---|---|
| `GET /health` | 服务状态、配置是否存在；不返回密钥 |
| `GET /catalog` | 8个候选地点的演示基础数据 |
| `POST /plan` | 按profile和mode生成草案，不保存 |
| `POST /weather` | 按profile日期获取Google天气，不保存或重排行程 |
| `GET /trips` | 已保存的行程 |
| `POST /trips` | `{ "trip": <草案>, "expectedRevision": 0 }`；首次保存revision1，修改时传当前revision |
| `DELETE /trips/{id}` | 删除指定行程 |
| `GET /memories` | 已保存的授权偏好 |
| `POST /memories` | `{ "consent": true, "summary": "喜欢艺术", "profile": {"interests":["艺术"],"maxSteps":5000} }` |
| `DELETE /memories/{id}` | 从可检索记忆撤回；不抹除追加审计历史 |
| `GET /setup` | 本机密钥配置表单；POST需要页面提供的CSRF令牌 |

规划请求示例（日期自行调整；live天气只支持未来10天）：

```sh
curl -s http://127.0.0.1:8787/plan -H 'Content-Type: application/json' -d '{
  "mode":"demo", "date":"2026-10-24", "days":1,
  "budget":100, "maxSteps":10000, "startMinute":600, "endMinute":1080,
  "interests":["艺术","风景"], "mustVisit":[], "request":"想多看看艺术馆",
  "start":{"name":"环形码头","lat":-33.8611,"lng":151.2126},
  "end":{"name":"环形码头","lat":-33.8611,"lng":151.2126}
}'
```

budget是每天澳元活动预算；时间用当地午夜起算分钟。约束无可行解返回422；保存版本冲突409；非法来源403。API不会预订、付款或提交赛事。

## 测试与 CI

```sh
# 先启动本机服务；HTTP测试只删除自己创建的记录
python3 -m unittest discover -s server/tests -v
# Node24，官方H-MEM conformance
HMEM_IMPL="$PWD/server/vendor/hmem/src/index.ts" \
  node --experimental-transform-types server/vendor/hmem/conformance/runner.ts
```

本机已通过21项后端测试和13项官方H-MEM测试，iOS模拟器构建/启动已验证。GitHub Actions会启动独立服务执行后端及H-MEM测试，不调用付费服务，也不冒充iOS云端构建验证。真实在线请求记录见[验证说明](docs/verification.md)。

## 项目结构

```text
ios/                   SwiftUI App、Xcode工程、配置模板
server/app.py          本机HTTP API与SQLite存储
server/planner.py      候选收集、Nemotron排序与确定性约束检查
server/weather.py      Google天气、日期匹配与失败处理
server/memory.py        Node24 H-MEM桥接
server/vendor/hmem/    官方参考实现、许可和来源
server/tests/          后端与HTTP测试
scripts/               启动和工程生成脚本
docs/                  App截图、演示录像、验证与参赛清单
.github/workflows/     后端自动检查
```

## 限制与黑客松状态

- 限定8个悉尼地标、步行模式，最多尝试8个候选顺序，不保证全局最优。Google常规营业时段不保证节假日正确；缺失数据使用明确标记的假设。
- 费用、步数、停留时间为估算；没有住宿、全餐饮、实时票价、排队、坡度和无障碍模型。
- 没有多人账户、社区、共享同步、支付、订票、酒店预订、航班追踪或后台GPS。出发准备徽章不等于到访认证。
- H-MEM为本机单用户；删除后不再检索，但追加审计账本保留历史。未启用自动dreaming/sleep。
- NVIDIA直连NIM和官方Meterless H-MEM已验证。**Cursor、Zetaris按团队要求暂缓，Nightshift未接入；不声称满足全部赛事要求。** 最终赛道、赞助商要求与HackOS提交仍需团队确认。[参赛清单](docs/submission-checklist.md)
- 视频是之前版本的原始录屏，没有新版任务引导、解说或全部增量功能；以源码与当前截图为准。演示稿未存入本仓库。

## 来源与许可

项目沿用[MIT许可证](LICENSE)。`server/vendor/hmem`为官方参考实现，保留独立Apache-2.0许可和[来源说明](server/vendor/hmem/UPSTREAM.md)。Google SDK及在线服务受各自条款约束。

[Google Maps iOS](https://developers.google.com/maps/documentation/ios-sdk/config) · [Places](https://developers.google.com/maps/documentation/places/web-service/text-search) · [Routes](https://developers.google.com/maps/documentation/routes/compute_route_directions) · [Weather](https://developers.google.com/maps/documentation/weather/overview) · [NVIDIA NIM](https://docs.api.nvidia.com/nim/reference/llm-apis) · [Meterless](https://github.com/Meterless/Meterless/tree/main/engines/hmem)

交互借鉴[Duolingo的小步学习、即时反馈和完成奖励](https://blog.duolingo.com/duolingo-teaching-method/)，使用原创Buddy角色；本项目与Duolingo无关联。
