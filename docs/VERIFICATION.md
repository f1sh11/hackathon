# 本地交付验证 · 2026-10-22

这是首次本地交付时的验证记录。仓库发布后的持续检查结果请查看 [GitHub Actions](https://github.com/f1sh11/hackathon/actions)。

## 已实际执行

- React + TypeScript 生产构建通过。
- 本机 .NET 10 后端编译通过：0 警告、0 错误。
- 纯 C# 规划器测试 13 项通过，包含 160 个固定随机种子的约束案例。
- Docker Compose 多阶段镜像构建成功，实际发布 .NET Release 应用。
- 实际运行 PostgreSQL 17 与 .NET API 容器，两者通过健康检查。
- 真实 HTTP 自动验收 44 项通过：画像只读、创建任务、预算/时间/HP、零预算、输入校验、重复请求、并发重规划、跨操作请求编号冲突、不可行任务、历史版本。
- 重启 API 与 PostgreSQL 容器后，画像编号/分数、3 个验收任务及版本计数保持一致。
- 真实浏览器测试通过：已保存画像、创建任务、完整行程、指标一致、轻松版、历史版本、刷新恢复、不可行任务、320px 布局；无浏览器运行错误。
- 前端独立测试另覆盖六题流程、选项门控和服务端派生画像。

## 用户试用入口

http://127.0.0.1:5080

本机为用户首次体验准备了独立空白数据库 `roam_local_mvp`，原验收数据库 `roam` 及其记录完整保留。没有通过删除历史数据来重置画像。

## 尚未声称完成的事项

- 首次本地交付时 GitHub Actions 尚未连接远端；当前状态以 Actions 页面为准。
- 无线上部署或生产 CD，当前交付是本地 Docker Compose。
- 无外部地图、天气、模型、预订 API。
- 自由文本备注只保存，不参与自然语言解析。
- 日期只保存，不处理真实节假日营业变更。
- 媒体只预留 PostgreSQL 元数据表，没有上传或视频处理功能。

## 复验

`dotnet run --project tests/Roam.Core.Tests --configuration Release`

`node scripts/smoke.mjs`（会创建带自动验收名称的本地任务；不会删除已有记录）

截图 `preview-itinerary.png`、`preview-mobile.png` 来自真实浏览器与真实后端；原始浏览器运行日志保留在本地，没有作为仓库源码发布。
