# 实际验证记录

验证日期：2026-10-22，Australia/Sydney。

## 已验证

- Xcode 27.1；Google Maps SDK for iOS 11.2.0 已通过 Swift Package Manager 解析。
- iOS Simulator 构建成功。App 安装并启动于 `Sydney Buddy Demo`（iPhone Duo / iOS 27.1）。
- 通过电脑控制实际点击四步引导、生成草案、确认保存和地图主页面；不是仅生成源码。
- NVIDIA 官方 endpoint 的 Nemotron 3.5 Lightning 返回成功；完整模型推荐与程序验证链路已运行。
- Google Places 真实返回 8 个悉尼地标；Google Routes 真实返回环形码头到歌剧院 664 米、10 分钟步行路线。
- Google + NVIDIA 完整在线规划成功：4 个地点、5 段真实路线折线、约 9,812 步，满足本次 10,000 步上限和返回时间。数字仅为这一测试输入的结果，不是通用性能结论。
- 服务端 key：当前网络出口 IP 限制，只允许 Places API (New)、Routes API 和 Weather API。
- iOS key：只允许 `com.sydneybuddy.travel`，只允许 Maps SDK for iOS。
- 已启用 Places、Routes、Weather 与 Maps SDK for iOS。用户本人完成了 Google 计费与 NVIDIA 登录/手机验证。
- 16 项规划与本机 HTTP 测试通过：预算、时间不可行、必须景点、雨天情景、多日去重、返程、未知营业时段、错误模型输出、模式失败、同意保存记忆、H-MEM 检索撤回、行程版本冲突、跨来源请求拒绝。
- Meterless 官方 H-MEM 参考实现的 13 项契约检查全部通过。
- 密钥保存在权限 0600 的本机文件，不进入分发源码压缩包。没有将 NVIDIA key 编译到 iOS App。

## 清楚保留的限制

- 上述步数、参观时间、预算仍为模型之外的启发式估算；非医学体力评估或商家报价。
- Google 常规营业时段不代表节假日特殊安排；缺失信息仍列为未核实。
- 可行方案采用限定候选集与最多8次真实路线校验，不能宣称全局最优。
- 本机单用户原型，未做账户体系、多人共享、真机远程后端或线上部署。
- Cursor、Zetaris 暂缓。Nightshift 未接入，采用主办方支持频道回复允许的 NVIDIA 直接接口方案。
- H-MEM 使用官方确定性参考实现及本机持久化；不是完整生产级记忆服务。撤回后不再检索，审计历史仍保留。
- API 接入成功不表示提交成功；未推送 GitHub、未提交 HackOS。

截图：`app-running.png` 为早期示意地图实机运行截图；后续 Google 地图截图若存在，以 `app-google-map.png` 为准。`google-key-restrictions.png` 不包含明文密钥。

## App 内在线验收

- 在 App 中开启在线服务，实际调用 Google 与 Nemotron。“少走一点”使预计步数由 9,219 下降为 6,182；草案确认后显示真实 Google 步行路线。
- 账本实际录入 AUD 90，3人均分结果为 AUD 30 / 30 / 30。
- 已录制 126.43 秒本机操作视频 `app-demo.mp4`，包含草案确认、Google 路线、行程管理、账本与连接状态；这是未经剪辑的操作录像，不含解说。
- 另有6页可编辑演示稿，保留为 Codex 中的演示文稿。

## Google Weather 增量验证 2026-10-22
- 已启用 Weather API，服务端 key 保留原 IP 限制并增加单个 Weather API 范围；iOS key 未改。
- 真实悉尼预报成功：10月22日小雨、18.3–22.7°C、降水概率70%；10月23日多云间晴、17.8–22.1°C、20%。天气会变化，以查询结果为准。单次成功请求实测0.75秒，不构成延迟保证。
- App 内点击刷新天气，已显示10月22日上述 Google 预报，见 app-google-weather.png。
- 21项后端测试通过，包含逐日降雨隔离、超范围、连接失败、缺失降水概率；iOS模拟器构建成功。

## Buddy 交互增量验证 2026-10-22
- iOS构建成功；在模拟器逐步走完8步引导，选择2天后Buddy立即回应，最终生成2天演示草案，未保存测试草案。
- 查看路线后准备进度0/3变1/3；真实天气刷新成功后变2/3。实际行李未替用户标记完成。
- 首页任务区默认收起，支持展开；同一行程进度跨App重启保留。
- 图片：buddy-onboarding.png、buddy-preparation.png（展开状态）。

## 发布边界
管理控制台截图包含账号和网络配置，未发布；App截图与演示视频随源码提供。本文历史构建和验证记录来自本机，不代表GitHub Actions已通过；CI结果请查看仓库Actions。
