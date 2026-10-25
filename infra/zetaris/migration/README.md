# 本機 Zetaris → AWS Zetaris：公開旅遊資料搬移包

雪梨雲端部署、三種 App 資料來源切換，以及從正在使用的來源資料庫匯出完整公開資料，請先看 [ZETARIS-SYDNEY.md](../../../docs/deployment/ZETARIS-SYDNEY.md)。下列工具只處理原有三份 CSV；新工具另支援即時來源快照與私有 S3 Filestore。

這套工具把目前專案的公開資料打成 ZIP，讓你在 **AWS 可連到的 PostgreSQL 來源資料庫** 匯入，再在 AWS Zetaris 登錄同名資料來源與天氣 REST Table。它不建立 AWS 資源、不自動上傳、不搬使用者資料，也不是 Zetaris 引擎／系統備份工具。

Zetaris 採資料虛擬化：景點資料實際放在 PostgreSQL，Zetaris 保存連線與表格登錄。**只搬 Zetaris 容器，AWS 仍讀不到原本電腦上的資料庫。** 因此這裡採「公開資料包 + 來源 DB 重建 + Zetaris 重登錄」，不用複製本機 Docker volume、管理帳密或平台安裝包。

```text
本機 3 份公開 CSV → ZIP + manifest + SHA-256
                             ↓ 手動送到 AWS 匯入機
                 AWS 專用 PostgreSQL（私有連線）
                             ↑ JDBC 唯讀
                        AWS Zetaris → Open-Meteo（每次查天氣）
                             ↑
                   App 後端 → iOS / Android
```

## 啟動教學入口

首次安裝、之後快速開關服務、AWS cluster 啟動及匯入順序，請看 [SERVICE-STARTUP.md](SERVICE-STARTUP.md)。這份教學也會放進 ZIP，目標機拿到資料包就能查閱。

## 1. 一個指令打包（本機）

在 **專案根目錄** 執行，僅需 Python 3 標準函式庫：

```sh
python3 scripts/package_zetaris_migration.py
```

輸出在 `infra/zetaris/bundles/`，檔名帶 UTC 時間，每次產生新檔，不覆蓋舊 ZIP。另有同名 `.zip.sha256`。輸出目錄已加入 `.gitignore`；工具會印出檔案位置及各資料集筆數。

可以指定檔名與來源：

```sh
python3 scripts/package_zetaris_migration.py \
  --source-dir infra/zetaris/data \
  --output infra/zetaris/bundles/travel-release.zip
python3 scripts/package_zetaris_migration.py --verify infra/zetaris/bundles/travel-release.zip
```

驗證會檢查檔案清單、SHA-256、CSV 格式、ID／座標一致性、重複 ID、資料集及筆數；會拒絕天氣快照與常見私密欄位。這不是完整的秘密掃描或匿名化工具。checksum 可偵測傳輸損毀，不能代替可信寄件者或數位簽章。

缺少 CSV 時，先依上層 [本機整合說明](../README.md) 準備資料。打包不會重新打 API、不會讀取任何 `.env`，也不會從線上的 Zetaris DB 匯出：**這是目前匯入來源 CSV 的版本，不是引擎內臨時修改的快照。** 先完成來源更新再打包。

## 2. 包裡有什麼

| 檔案 | 用途 |
| --- | --- |
| `data/travel-records.csv` | Overture 地點、8 個示範候選、底圖供應商中繼資料、來源版本 |
| `data/hidden-gems.csv` | 社群景點座標、到達介紹、照片連結與逐筆授權；有資料才有評論 |
| `data/osm-map-places.csv` | OSM 景點、餐飲、住宿、商家與設施 |
| `manifest.json` | 實際筆數、每個檔案大小／SHA-256、打包時間、本機測試版號 |
| `verify_bundle.py` | 離線檢查 ZIP 或解壓後資料夾 |
| `restore_source.py` / `restore.sql` | 驗證後，在單一交易中匯入全新的專用 PostgreSQL DB |
| `grant-reader.sql` | 授權既有唯讀登入角色讀取六個 views |
| `register.template.sql` | AWS Zetaris 的 PostgreSQL JDBC 登錄範本 |
| `weather.sql` | Zetaris 原生即時天氣 REST Table；沒有快取指令 |
| `insights.sql` | NSW 路況與 OSM 雪梨收費的即時 REST 來源登錄（不含人潮或 Google 私密金鑰） |
| `verify.sql` | AWS Zetaris 驗收查詢 |
| `SERVICE-STARTUP.md` | 本機／AWS Zetaris 啟動、來源 DB、App 接線與停止教學 |
| `LICENSES.md` / `README.md` | 來源授權與這份操作說明 |

不含：本機管理／來源 DB 密碼、AWS 金鑰、App 使用者與行程、偏好與聊天、資料庫 volume、Zetaris 映像或 JDBC 授權安裝包、圖片原檔、全景原檔、地圖圖磚或導航路線。底圖中繼資料不等於可離線使用 Apple／Google 地圖。

## 3. 傳送到 AWS（自行選一種方式）

先用上面的 `--verify` 驗證 ZIP。可透過既有安全檔案傳輸／SSM 工作流程送到匯入機，或選用你自己 AWS 帳號的 **私有 S3 bucket**：

```sh
# 只是一個範例，請換成自己的檔名、bucket 與 prefix；本工具不會執行上傳。
aws s3 cp infra/zetaris/bundles/travel-release.zip s3://YOUR_PRIVATE_BUCKET/zetaris-import/travel-release.zip
aws s3 cp infra/zetaris/bundles/travel-release.zip.sha256 s3://YOUR_PRIVATE_BUCKET/zetaris-import/travel-release.zip.sha256
```

不需要把 bucket 設公開，也不要把 ZIP 或 DB 密碼放進 Git。若用 S3，這裡只是傳輸媒介；**AWS Zetaris 仍登錄下方 PostgreSQL views，並非直接讀這個 ZIP。**

目標機拿到兩個檔案後，在同一目錄：

```sh
shasum -a 256 -c travel-release.zip.sha256
# 建議先從這份專案取得可信的驗證工具，在解壓前驗證：
python3 /path/to/trusted/package_zetaris_migration.py --verify travel-release.zip
mkdir travel-release
unzip travel-release.zip -d travel-release
python3 travel-release/verify_bundle.py --verify travel-release
```

請解壓到全新資料夾，不要把私人設定或額外檔案放進包內。驗證器會拒絕額外檔案、符號連結、異常 ZIP 路徑及超過 100 MiB 的資料包。

## 4. AWS 來源資料庫準備與匯入

需要：Python 3、PostgreSQL `psql` 客戶端、**已存在而且全新的專用 DB**（範例名稱 `travel_public`），及能在裡面建表的匯入角色。可用 RDS PostgreSQL 或你們私有網路的 PostgreSQL；不是 Zetaris 本身的系統 metastore。DB 名稱不是自動新建的。

AWS Zetaris 的運算節點及匯入機必須能透過 VPC 路由、DNS 和 Security Group 連到此 DB；僅開放必要來源到 DB 連接埠。`localhost`／`host.docker.internal` 不能當 AWS 運算節點連回你電腦的方式。讓 DBA 設好私有主機名、TLS 信任鏈與獨立角色，勿直接暴露 DB 到公網。

在包外設定 libpq 的 `PGSERVICE`／`PGPASSFILE` 或標準環境變數。以下沒有密碼：

```sh
export PGHOST='YOUR_PRIVATE_DB_HOST'
export PGPORT=5432
export PGUSER='YOUR_IMPORT_ROLE'
export PGSSLMODE=verify-full
export PGSSLROOTCERT='/secure/path/aws-rds-ca-bundle.pem'
export PGPASSFILE='/secure/path/travel.pgpass'
# pgpass 由你在目標機私下建立，權限設為只有本人讀寫；不要加入 ZIP。
python3 travel-release/restore_source.py --database travel_public
python3 travel-release/restore_source.py --database travel_public --execute
```

第一個指令只驗證包，不連 DB。第二個才寫入。可改 DB 名稱，但下一步 JDBC URL 也要一致。

匯入在單一交易中建三張實體表與六個 views、載入三份 CSV、核對 manifest 筆數才提交。**不會 DROP／DELETE／TRUNCATE 既有資料。** 同名表已存在會整批回滾；不是增量更新工具。重複演練請 DBA 給另一個全新專用 DB，不要為了重跑去刪正式 DB。若失敗先檢查 PostgreSQL 原始錯誤與權限。

接著 DBA 另建 AWS 專用的 `travel_reader` 登入角色，私下設定新密碼（不要沿用本機密碼），再於解壓目錄執行：

```sh
PGDATABASE=travel_public psql -X -v ON_ERROR_STOP=1 \
  -v reader_role=travel_reader -f grant-reader.sql
```

角色只需要 CONNECT、schema USAGE、六個 views 的 SELECT。匯入角色與 Zetaris 的唯讀角色應分開，DBA 可再依組織規範限制公共 schema 權限。

## 5. 在 AWS Zetaris 登錄

使用 **目標 AWS cluster 自己** 的 Gateway／組織帳號；不是本機 Docker 帳號。先確認 cluster Active，進入 SQL Workspace，執行 `SHOW DATASOURCES`、`SHOW LIGHTNING DATABASES`。此範本以本機 2.4.3.1 語法為基礎，AWS 版需確認 PostgreSQL JDBC driver 與 Lightning REST Table 功能可用；不能假設任意雲端版本完全相同。

1. 若不存在 `sydneybuddy_travel`：在目標 Gateway 的 PostgreSQL 連線設定輸入私有 DB endpoint、DB 名稱、`travel_reader` 和目標密碼，並採用平台支援的秘密儲存方式。也可使用 `register.template.sql`，將 `REPLACE_PRIVATE_DB_HOST`、DB 名稱及 `REPLACE_PRIVATELY` 在目標安全環境填妥；不要把填入秘密的 SQL 存回專案或包內。JDBC TLS 預設 `sslmode=verify-full`，要讓引擎信任 DB CA；不要用關閉驗證來繞過問題。
2. 登錄範本列出的六個 views，保持 `sydneybuddy_travel.travel_*` 名稱，讓 App 資料模型一致。若改 schema／namespace，須同步調整 App 後端固定查詢。
3. 若 datasource 已存在，先比對 JDBC host、DB、schema、讀取角色及表格內容。**不要直接貼 CREATE 全部重跑或覆蓋既有來源**；僅補缺少的登錄，或先用另一個測試組織／namespace。
4. 依 `weather.sql` 建立缺少的 `sydneybuddy_live`／`weather_sydney`。如果 database 已存在，跳過 CREATE DATABASE；若表已存在，先確認 URL 是否相符。需允許引擎對 Open-Meteo 的 HTTPS 連線。這份範本固定雪梨、七日預報。
5. 依 `insights.sql` 登錄缺少的 `traffic_nsw`／`fees_sydney` 公開來源。這是道路事故與 OSM 參考收費，不是即時車速、人潮或完整餐廳菜單。
6. 天氣不執行 `CACHE TABLE`，不匯入 forecast CSV。已有天氣快取請先移除。連續查兩次並用引擎或來源請求紀錄確認真的發出兩次 HTTP；相同預報數值不代表沒有發請求。

六個公開靜態 views 可依平台能力快取，但來源更新後要重新整理；不要把天氣一起快取。本機曾遇到未快取 JDBC filter／LIMIT 的計數問題，因此 App 現在讀取有上限的完整公開集合後篩選，並非全球大資料查詢方案。

## 6. 驗收與手機接線限制

在目標 Zetaris 執行 `verify.sql`，把六組 COUNT 與包內 manifest 對照，並抽看 payload 的座標、照片署名與到達介紹。這次包的筆數由 manifest 計算，不在 SQL 裡寫死。

完成資料匯入後，**不代表手機已切到 AWS**：目前專案後端的 Zetaris 連線限制為本機 loopback HTTP，iOS／Android 仍呼叫自己的 App 後端。下一步需另外設定／實作後端的 AWS HTTPS 或內網連線、目標登入／秘密管理、組織 ID、TLS 及版本對應 API，再把手機 API 位址改為可達的後端位址。不要把 Zetaris 管理密碼放到手機，也不要為了接雲端放寬任意 SQL 或關閉 TLS。

本工具只建立搬移材料並驗證公開資料。尚未取得你們 AWS endpoint／目標版本及連線設定，**不會宣稱已完成 AWS 匯入或手機雲端驗收**。

## 故障對照

| 狀況 | 檢查 |
| --- | --- |
| ZIP 檢查失敗 | 重新傳輸；確認沒有手動修改 CSV／SQL 或在解壓目錄新增檔案 |
| relation already exists | 目標不是空白專用 DB；回滾後改用新的專用 DB |
| AWS Zetaris 讀不到 DB | 私有 DNS、VPC 路由、SG、DB 角色、TLS CA；不能填 host.docker.internal |
| CREATE / REGISTER 語法不支援 | 目標版本／driver／REST 功能與本機不同，依目標 UI／官方文件調整 |
| 天氣失敗 | 引擎對外 HTTPS、Open-Meteo 可用性、REST Table 設定；不要用舊預報冒充 |
| DB COUNT 正確而引擎 COUNT 異常 | 檢查 JDBC 引擎快取／filter 下推；比對完整 payload 集合 |
| 圖片或評論沒有出現 | 包內只含來源實際提供的照片中繼資料；沒有評論就保留空陣列 |

參考：[Zetaris Connection 概覽](https://kbase.zetaris.com/knowledge/connect)、[SQL Manual](https://kbase.zetaris.com/knowledge/sql-manual)、[AWS PostgreSQL 連線與 TLS](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/PostgreSQL.Concepts.General.SSL.html)。雲端管理介面的步驟及服務權限以你們目標版本為準。
