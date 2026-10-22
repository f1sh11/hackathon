# Sydney Buddy · React Web

中文：这是连接真实 ASP.NET Core API 的 React + TypeScript 网页客户端，适配手机和桌面浏览器。原 Android/iOS 客户端保留在仓库其他目录。

English: A responsive React + TypeScript client for the real ASP.NET Core API. The existing Android/iOS clients remain separate.

```sh
npm ci
npm run dev
```

开发入口 / Development URL: `http://127.0.0.1:5173`。Vite 将 `/api/*` 请求代理到 `http://127.0.0.1:8787/*`。先启动 PostgreSQL 与后端；配置方法见仓库根目录 README。

```sh
npm test
npm run build
```

构建输出为 `dist/`，ASP.NET Core 可在同一域名提供这些静态文件。/ Production assets are written to `dist/` and can be served by ASP.NET Core on the same origin.

## 模块职责 / Module responsibilities

| 文件 / File                         | 职责 / Responsibility                                                                                                |
| ----------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `src/components/AppLayout.tsx`      | 页面框架、导航和状态提示 / Page shell, navigation and status display                                                 |
| `src/components/SavedTrips.tsx`     | 行程集合与本地删除确认 / Trip collection and local delete confirmation                                               |
| `src/components/ProposalBanner.tsx` | 草案差异展示与确认入口 / Draft comparison and confirmation controls                                                  |
| `src/App.tsx`                       | 协调加载、生成草案、确认保存及版本快照 / Orchestrates loading, draft generation, confirmation and revision snapshots |
| `src/api.ts`                        | 网络错误、超时和响应边界 / Network requests, errors, timeouts and response boundary                                  |
| `src/types.ts`                      | 与 API 一致的 TypeScript 数据契约 / TypeScript wire contracts matching the API                                       |
| `src/domain.ts`                     | 纯函数：偏好验证、保存载荷、整数分金额 / Pure preference, save-payload and integer-cent money helpers                |
| `src/hooks.ts`                      | 浏览器本机偏好、准备清单、账本存储 / Browser-local preference, preparation and ledger persistence                    |
| `src/components/PlannerForm.tsx`    | 收集用户偏好，不执行保存 / Collects preferences without saving records                                               |
| `src/components/TripView.tsx`       | 展示行程、天气与证据来源 / Renders itinerary, weather and evidence                                                   |
| `src/components/RouteSketch.tsx`    | 显示位置示意及 Google Maps 链接 / Shows schematic positions and Google Maps links                                    |
| `src/components/Essentials.tsx`     | 本机准备清单、分账，以及授权偏好操作 / Local checklists, expenses and consented memory actions                       |
| `src/components/Icon.tsx`           | 共享装饰图标 / Shared decorative icons                                                                               |

## 关键行为 / Key behavior

- 草案只有点击“确认并保存”才提交。取消草案不会改变已保存行程。/ Only explicit confirmation saves a draft; cancellation preserves the saved trip.
- 修改时使用生成草案时捕获的 `expectedRevision`，HTTP 409 要求刷新后重新调整。/ Adjustments preserve the captured revision; HTTP 409 requires reload and a fresh adjustment.
- `demo` 不调用付费服务；`assisted` 调用 NVIDIA，但其他数据仍为示例；`live` 请求实际 Google 服务。/ Modes distinguish demo data, AI-assisted examples and actual provider calls.
- 地图始终明确标记为位置示意，连线不是步行导航。/ The local map is explicitly schematic; lines are not walking navigation.
- 本客户端不接收或保存 API 密钥。在线配置留在服务端。/ The client never receives or stores API keys; configuration remains server-side.
- 旅行偏好需勾选同意才保存，可随时撤回；历史审计记录仍保留。/ Travel memories require consent and can be revoked; historical audit records remain.
- 账本按整数分分摊，准备清单与账本只保存在当前浏览器。/ Expenses use integer-cent shares; checklists and ledger stay in this browser.

首次接手建议阅读 / Suggested review order: `App.tsx` 的 `generate` / `acceptProposal`，然后 `domain.ts` 的 `savePayload` / `parseCents`，最后看 `api.ts` 的网络边界。

代码格式 / Formatting: `npm run format`；校验格式 / Check formatting: `npm run format:check`。
