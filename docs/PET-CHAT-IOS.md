# iOS Buddy 宠物对话与记忆

本分支基于 `feat/personalized-schedule-editor`，仅更新 iOS 和共用 .NET 后端。Android 界面未改动。

## 使用

1. 启动 PostgreSQL 和 .NET API，按 README 配置本机连接字符串。设置 `NVIDIA_API_KEY` 环境变量或仓库根目录被忽略的 `.secrets.json`；`NVIDIA_MODEL` 可选。H-MEM 需要 Node 24，必要时设置 `NODE_BINARY`。
2. Xcode 打开 `ios/SydneyBuddy.xcodeproj`，运行 SydneyBuddy。
3. 首次打开或在行程页点“新增”，进入宠物引导。选择“点选回答”或“自由聊”，随时切换，共用已收集的信息。
4. 首次引导答完后会固定显示“生成行程并打开地图”，点击后生成、保存新行程并进入地图。“检查旅行信息”仍可编辑草稿；已有行程的调整继续先生成待确认草案。现有路线引擎支持悉尼 1–3 天；其他城市只收集资料，禁止生成错误城市路线。界面明确区分在线 AI 聊天与示例路线。
5. 已保存行程的地图页点“🐨 聊一聊”继续；每个行程使用独立会话。“我的记忆”展示本次旅行字段与长期偏好。

## 保存与数据范围

- iOS UserDefaults 只保存会话 ID、点选/聊天模式和引导状态；完整对话在后端 `server/.pet-sessions/`，文件 0600、目录 0700（Unix），已 Git 忽略。每份草稿/行程独立，进度可跨 App 和服务重启恢复。
- 最近 20 条消息进入 AI 上下文；每会话保留最多 100 条消息，结构化旅行信息独立保留。
- 用户点击“以后都记住”才写入 H-MEM `long_term` 和 PostgreSQL 偏好展示记录。选择“仅限这次”只写本次草稿。没有确认的建议不会成为长期偏好。
- 修改长期偏好会替换旧 H-MEM 记录；撤回后不再出现在后续对话的有效偏好列表。H-MEM 保留审计历史。旧聊天文字仍可在当前历史中显示，系统指令要求只采用当前有效偏好列表。
- “清空本次对话”删除该会话及旅行草稿，保留已确认行程和长期偏好。新建旅行不会携带上次目的地/日期/预算。
- 自由聊天将当前旅行字段、最近对话和已确认长期偏好发送到 NVIDIA。固定选项不调用模型。密钥仅在服务端使用，不进入 iOS 工程或 App。
- NVIDIA 聊天使用独立、固定域名的客户端；配置该密钥即可聊天，不会启用 Google 付费服务。原有 `AllowPaidServices` 规划/地图策略保持原行为。

## 协议

- `POST /pet/sessions`：创建草稿，可携带初始 `fields`。
- `GET/PATCH/DELETE /pet/sessions/{id}`：恢复、编辑或清空。
- `POST /pet/sessions/{id}/turn`：`message`、UUID `requestId`；固定选项增加 `field`、`value`。NDJSON 流包含 `reply`（累计文字）、`done`（完整状态）或 `error`。同一 requestId 重试不会重复记入历史。流中断/模型格式错误不会提交半成品状态。
- `POST /pet/sessions/{id}/memory`：`consent=true` 长期保存，false 仅本次。
- `PUT /memories/{id}`：用户确认后替换偏好；原 GET/POST/DELETE 接口继续兼容。

## 验证

后端 PetConversationTests 使用隔离 PostgreSQL schema、假模型验证模式切换、消息重试、服务重启恢复、新旅行隔离、记忆同意/修改/撤回以及失败流不提交。PetProtocolTests 覆盖跨 token 的 JSON 转义。iOS PetDraftTests 验证路线信息转换、无效日期/预算、城市限制和恢复状态解码。离线自动测试不调用 NVIDIA；真实 AI 与 H-MEM 另行执行端到端 smoke test。

## 当前范围

文字聊天与流式传输、原生矢量考拉及状态动画；没有麦克风实时语音。后端仍为本机单用户，多个客户端共用长期偏好；没有账号隔离或跨设备云同步。每个模型响应到达的分块粒度取决于提供方，客户端不会伪造 token 流。
