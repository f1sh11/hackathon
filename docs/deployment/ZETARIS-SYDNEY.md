# Zetaris 雪梨雲端與本機切換

本次工作位於 `feat/zetaris-sydney-cloud` 獨立工作目錄，沒有修改原 checkout 未提交的照片功能。

## 架構

```
iOS / Android → ASP.NET Core
                    ├ direct → 原始公開供應商
                    ├ zetaris → 本機 Zetaris → 本機公開來源 PostgreSQL
                    └ zetaris-cloud → 雲端 HTTPS Zetaris → AWS 私有來源 PostgreSQL
```

App 設定中的資料來源選擇以 `X-Travel-Data-Source` 傳遞。手機不保存 Zetaris 帳密、不發 SQL、不提供任意連線網址。既有 `zetaris` 值仍代表本機。三種來源的回應保留同樣的 `dataSource` 識別，切換時丟棄舊來源的搜尋回應。

雲端不能連線時明確報錯，不靜默改讀本機。靜態集合的短期記憶體快取依本機／雲端、API、登入端點、組織與帳號隔離；天氣每次透過所選來源即時查詢，不讀快取快照。

## 已建 AWS 基礎設定（不是運算服務）

- 區域：Sydney `ap-southeast-2`。
- CloudFormation：`zetaris-Trip-App-8046a945`，2026-10-25 已確認 CREATE_COMPLETE。
- Zetaris credential 辨識名稱：`Trip App`，已 Verified。
- 官方範本版本：1.0.0.6。
- Mini 預備名稱：`sydneybuddy-demo`，2 × m7i-flex.large，是否實際啟動以 Cloud Gateway 為準。
- 建立基礎堆疊包含 NAT、VPC、子網路、S3、跨帳號角色；即使尚未建立 cluster，部分資源已可能計費。
- 使用者最初核准 US$100／月；Mini UI 顯示美東範例暫停仍約 US$130／月、運作約 US$170–280／月，超出原預算。雪梨價格不同，不能把這個數字當正式報價。
- 停止 cluster 不代表所有帳單停止。資料搬移、AWS 執行與手機雲端驗收必須分開記錄。

## 後端設定

既有 `Zetaris__BaseUrl`、`Zetaris__LoginUrl`、`Zetaris__Username`、`Zetaris__Password`、`Zetaris__EnvironmentFile` 仍適用本機。也可改用 `Zetaris__Local__*`。本機僅接受 loopback HTTP。

雲端使用完全獨立的環境變數：

```
Zetaris__Cloud__Transport=ui-proxy
Zetaris__Cloud__BaseUrl=https://YOUR_CLUSTER_GATEWAY_HOST
Zetaris__Cloud__LoginUrl=https://YOUR_CLUSTER_GATEWAY_HOST/api/auth/login
Zetaris__Cloud__Username=<目標組織專用帳號>
Zetaris__Cloud__Password=<私下以 Secrets Manager 或執行環境注入>
Zetaris__Cloud__OrgId=<目標登入回應的組織 ID>
Zetaris__Cloud__RestaurantsEnabled=true
Zetaris__Cloud__SocialEnabled=true
```

`appsettings.ZetarisCloud.example.json` 只是範本，ASP.NET Core 不會自動載入 `.example.json`。請以環境變數或正式部署設定提供，勿提交填入密碼的副本。雲端不讀本機 `.env`，不接受明文 HTTP、loopback、URL 內的帳密、query 或 fragment。HTTP redirect 不跟隨；TLS 使用平台的標準憑證驗證。

目前雲端連接器實作既有 Gateway 的登入 `idToken`/`orgId` 與 SQL Editor 回應格式；不同版本必須以真實雲端端點驗證，不能以 mock 測試宣稱已連線。

## 完整公開來源 DB 搬移包

舊 `package_zetaris_migration.py` 仍只打包三份來源 CSV。新工具讀取**實際公開來源 DB** 的六張表，排除 weather dataset，保留最新已同步的社群、餐廳及活動參考，不包含 App 使用者資料庫或 Zetaris metastore。

在有 `.env.local` 與來源 DB 的專案上：

```sh
python3 scripts/zetaris_source_snapshot.py \
  --export infra/zetaris/bundles/sydney-live-source.zip
```

若程式工作目錄與本機來源 checkout 不同，加上 `--source-project /path/to/original/repo`。不讀來源密碼到程式中，沿用 Docker Compose 注入；不修改來源 DB。匯出使用單一 repeatable-read 交易。若 public 表格結構超出明列六張表，拒絕匯出，避免漏資料卻誤稱完整。

包內僅有 JSON 資料及 manifest，附外部 SHA-256 檔；驗證器不執行包內 SQL。照片目前搬的是網址、署名、授權與介紹，不下載圖片、全景、地圖圖磚。

本次產出 `infra/zetaris/bundles/sydney-live-source-20261025.zip`：

| 表 | 筆數 |
|---|---:|
| travel_records（不含 weather） | 5981 |
| hidden_gem_records | 12 |
| osm_map_records | 5318 |
| social_place_records | 0 |
| restaurant_place_records | 0 |
| activity_offer_records | 16 |

0 表示本機公開來源尚無已同步資料，不代表原 App 私有資料庫沒有匯入紀錄。這六張表有重疊來源，不能把加總當成去重景點數。

### AWS 還原

先準備同區域、可由 Zetaris 私有網路存取的 PostgreSQL 及一個**全新空白專用 DB**。來源不是平台系統資料庫。匯入者先以 PGSERVICE/PGPASSFILE 或執行環境設定 host、登入及 AWS RDS CA：

```sh
export PGHOST='YOUR_PRIVATE_SOURCE_DB'
export PGPORT=5432
export PGUSER='YOUR_IMPORT_ROLE'
export PGSSLMODE=verify-full
export PGSSLROOTCERT='/secure/path/rds-ca.pem'
export PGPASSFILE='/secure/path/travel.pgpass'
python3 scripts/zetaris_source_snapshot.py --restore /secure/path/sydney-live-source.zip
python3 scripts/zetaris_source_snapshot.py --restore /secure/path/sydney-live-source.zip \
  --database travel_public --reader-role travel_reader --execute
```

第一行只驗證包。第二行才寫入，要求 `verify-full`，拒絕非空 DB；在單一交易建立六張表、九個 views、載入並核對筆數，任一失敗回滾。`travel_reader` 需由 DBA 事先建立並私下設定密碼，工具不建登入帳號。不要在既有正式 DB 覆蓋還原。

在雲端 Zetaris 用原 `register.template.sql` 登錄六個 base views，再補：

```sql
REGISTER DATASOURCE TABLE "travel_social_places" FROM sydneybuddy_travel;
REGISTER DATASOURCE TABLE "travel_restaurant_places" FROM sydneybuddy_travel;
REGISTER DATASOURCE TABLE "travel_activity_offers" FROM sydneybuddy_travel;
```

存在者先核對，勿盲目重跑。天氣與路況依 `infra/zetaris/migration/weather.sql`、`insights.sql` 另建即時 REST Table，天氣不 CACHE。

## App 使用

在「我的 → 旅遊資料來源」選「直接來源」「本機 Zetaris」「雲端 Zetaris（雪梨）」。兩種 Zetaris 共用 App 後端，由後端連到對應平台。

手機後端位址與 Zetaris 來源是兩個不同設定：iOS 已支援 `SydneyBuddyBackendURL`；Android 在私有 `local.properties` 設 `BACKEND_URL=https://YOUR_APP_API_HOST` 後重建。預設 Android 仍走模擬器 loopback 與 adb reverse；不可把 Zetaris 管理介面當 App API。

公開部署 App API 前仍需設定應用程式登入、HTTPS 與權限。此變更沒有把未驗證的開發後端直接公開到網際網路。

## 驗收

1. 雲端 cluster Active，使用目標組織登入。
2. AWS 來源表 COUNT 對照 manifest；PostgreSQL 方案檢查九個 views，S3 方案只登錄 manifest 中非空的表（本次為七張）。
3. Zetaris 查詢同名資料表，抽查座標、圖片署名與票價。
4. App 分別選三種來源，確認 `/data/status`、搜尋、票價、天氣回應的 dataSource 一致。
5. 連續查兩次天氣，核對來源請求記錄；斷開雲端確認明確錯誤、未改用本機。
6. 登記實際測試結果、AWS 保留儲存及計費資源，再決定停機或清理。

官方參考：https://github.com/zetaris/getting-started/blob/main/docs/install/managed-aws-cluster-guide.md

## 測試期間保留（使用者最新要求）

2026-10-25 使用者改為：建立後先讓本人及夥伴測試，等明確通知測試完成才刪除。不要自動暫停、刪除 cluster 或基礎堆疊。保留期間包含暫停狀態都可能計費，先前 US$100／月不能視為足以長期維持 Mini 的保證。

完成測試後，依序檢查 cluster、CloudFormation、NAT、負載平衡器、EFS、S3 等本次新增資源；刪除運算本身不保證所有費用停止。永久移除資料須依當時的具體資源確認，不碰既有或共用資源。

若將 .NET 建置輸出放到暫存目錄，可設定 `SYDNEYBUDDY_TEST_REPO` 為本專案完整路徑，供 fixture 找到來源檔案；此變數只用於測試。

## 短期驗證可用既有私有 S3，避免另外起 RDS

`prepare_zetaris_cloud_files.py` 將已驗證的 snapshot 轉成 JSON-lines，逐列保留 `id` 與原始 `payload`，並產生 Lightning Filestore SQL。它不自動上傳。先從 CloudFormation Outputs 確認工作區 bucket，使用自己私有的 prefix，不修改 bucket 公開存取設定。

```
python3 scripts/prepare_zetaris_cloud_files.py --snapshot infra/zetaris/bundles/sydney-live-source-20261025.zip --output infra/zetaris/bundles/s3-release --s3-prefix s3://YOUR_PRIVATE_WORKSPACE_BUCKET/sydneybuddy-public/20261025
```

零筆的社群／餐廳集合在 manifest 明列，不建立無法推斷 schema 的空檔案表；這種部署將對應 `Cloud:SocialEnabled`、`Cloud:RestaurantsEnabled` 設為 false。非空表在雲端逐一註冊，核對筆數才算搬移完成。若實例角色無法讀私有 S3，不改公開 bucket、不寫入 AWS access key，先檢查該實例的來源設定。

雲端 Gateway 可使用 `Transport=ui-proxy`，其 SQL 路由為 `/api/proxy/sql-editor/sqls/run-query`；本機舊模式仍是直接 API。兩者都能使用 `ApiKey` 與 `OrgId`，或使用明確設定的登入帳密。建議正式測試以只讀應用程式帳號／token 執行固定查詢；不要在手機或聊天提供 token。

## 本次實際進度（2026-10-25）

- 基礎堆疊 `zetaris-Trip-App-8046a945` 已 `CREATE_COMPLETE`，AWS 連線已驗證。
- Mini Trial cluster `sydneybuddy-demo`（`a6c43f1d-2765-487d-89af-99b612b39de9`）已建立申請，部署中；尚未驗證雲端 SQL 與手機端連線。
- 私有 bucket `zetaris-trip-app-8046a945-workspaces3bucket-3xphap9ctgz8` 的 `s3-release/` 已上傳 9 個檔案，共約 10.1 MB，全部成功。保留 Block Public Access；尚需在雲端註冊表格並核對筆數。
- 本機 PostgreSQL 真實還原測試核對六張來源表筆數成功；一次性測試 DB 已清除，原來源未變更。
- 後端 386 項、iOS 170 項、Python 20 項測試通過；Android 建置與單元測試通過。這些結果不代表雲端端到端驗證已完成。
- 獨立本機測試後端 `127.0.0.1:8788/data/status` 已實測：`direct` 回傳 200；`zetaris` 回傳 200，讀到 5968 個 POI、12 個秘境、5318 個 OSM 地點；尚未提供雲端設定的 `zetaris-cloud` 回傳 422，不會偷用本機來源。既有 `8787` 後端保留。
- 天氣維持即時 API，不在搬移包中。

測試環境保留至本人與夥伴測試完成、使用者明確通知清理為止。
