# Zetaris 本機與 AWS 啟動教學

本文件隨公開資料 ZIP 一起提供。先起服務，再依 [搬移操作文件](README.md) 匯入資料與登錄資料來源。以下「專案根目錄」指 Git checkout；ZIP 是資料搬移材料，不是完整 App 或 Zetaris 安裝程式。

## 先分清楚三個服務

| 服務 | 做什麼 | 本機入口 |
| --- | --- | --- |
| 官方 Zetaris 平台 | SQL／資料虛擬化、API、管理介面 | UI 3000、API 8888、JDBC 10000（目前安裝） |
| 本專案公開來源 DB | 儲存 Overture／OSM／Wikivoyage 公開資料 | PostgreSQL 55433 |
| App 後端 | 給手機的 API、行程與資料來源切換 | 8787；另有使用者 DB 55432 |

`compose.source.yaml` 只會起公開來源 DB，不能拿來當 Zetaris。官方平台自己的 PostgreSQL 是系統資料庫，不是匯入旅遊 CSV 的位置。

## A. 在本機起 Zetaris

### A1. 第一次安裝

先在 [Zetaris Run locally](https://cloud.enterprise.zetaris.com/dashboard/download) 取得你帳號有權使用的 `zetaris-platform.zip`、配套 `.env` 與 registry 登入方式。官方 getting-started repository 提供教學和資料配方，並不等於平台映像的使用權。沒有下載或映像權限時需向官方取得，不能用本專案的資料包取代。

安裝 Docker／Compose，啟動 Docker daemon；依官方版本的需求配置記憶體、磁碟與 Apple Silicon 相容設定。這台電腦目前把正式配套檔案放在 `$HOME/zetaris-local/zetaris-platform`，可避免 Documents 同步造成啟動等待。換電腦時使用自己的安裝路徑。

1. 將官方 ZIP 解壓到專用目錄，確認 `docker-compose.yml`、`.env`、`preflight.sh`、`verify.sh` 在同一層。
2. 依下載頁提供的指令登入官方映像 registry；不要把登入 token 存進專案、聊天或 Git。
3. 在平台目錄依序執行：

```sh
chmod 600 .env
bash ./preflight.sh
docker compose config --quiet
docker compose pull
docker compose up -d
docker compose ps --all
bash ./verify.sh
```

保留配套映像版本和 `.env`，不要拿本專案的 `.env.example` 覆蓋平台設定。初始化完成後，直接改 `.env` 的管理密碼不會重設已有帳號；登入資料要與真正平台帳號一致。

### A2. 之後快速啟動／停止

在專案根目錄：

```sh
bash scripts/zetaris_local.sh start
bash scripts/zetaris_local.sh status
bash scripts/zetaris_local.sh verify
# 暫停時保留資料：
bash scripts/zetaris_local.sh stop
```

若安裝位置不同，在命令最後加上路徑（含空格要加引號）：

```sh
bash scripts/zetaris_local.sh status "/path/to/zetaris-platform"
```

腳本不下載授權包、不登入 registry、不改密碼、不刪除 volume，也不操作 AWS。`start` 完成不等於資料就緒，仍需檢查服務健康及官方 `verify.sh`。不要執行 `down -v`，那會刪除 volume。

### A3. 啟動本專案資料與 App

在專案根目錄，首次才由 `infra/zetaris/.env.example` 準備私人的 `.env.local`、填入兩個不同的來源 DB 密碼。已有 `.env.local` 時保留它，不重新生成。若 volume 已存在，改 env 不會自動輪替 DB 密碼。

```sh
python3 scripts/prepare_zetaris_data.py
python3 scripts/import_hidden_gems.py
python3 scripts/import_osm_map_places.py
docker compose --env-file infra/zetaris/.env.local -f infra/zetaris/compose.source.yaml up -d --wait
```

`prepare_zetaris_data.py` 需要既有 Overture 快照；新 checkout 尚無資料時，先依專案 README 的 Overture 匯入步驟建立 `infra/poi/data/places.json`。兩個社群匯入器會連公開來源，來源暫時不可用不代表附近沒有地點。

接著依 [本機整合說明](https://github.com/f1sh11/hackathon/blob/main/infra/zetaris/README.md) 的三個 `psql` 匯入命令、官方 JDBC JAR 註冊流程與 `configure_zetaris_travel.py` 設定六個公開 views 和天氣。再執行：

```sh
python3 scripts/configure_zetaris_insights.py --platform-env "$HOME/zetaris-local/zetaris-platform/.env" --register
Zetaris__EnvironmentFile="$HOME/zetaris-local/zetaris-platform/.env" dotnet run --project server-dotnet/SydneyBuddy.Api
```

App 後端還需要本專案使用者 DB 55432 已啟動，請沿用主 README 的後端啟動流程。手機在「我的 → 旅遊資料來源」選 Zetaris；Android 模擬器使用 `adb reverse tcp:8787 tcp:8787`。目前手機 API 指向本機 loopback，真機需另設可達的後端位址。

## B. 在 AWS 起 Zetaris（官方 managed cluster）

建議使用官方 Cloud Gateway 的 AWS cluster 工作流程；這不是把 Docker Desktop 的容器整個上傳。以下依官方指南整理，實際欄位以你帳號當前介面為準。這份更新只提供教學，沒有建立、啟停或刪除你的 AWS 資源。

| 階段 | 在哪裡做 | 完成條件 |
| --- | --- | --- |
| 帳號接線 | Cloud Gateway 的 Compute → Credentials | 目標 AWS 帳號通過驗證 |
| 基礎資源 | 官方 Quickstart 導向的 CloudFormation | 在正確 AWS 帳號／region 完成建立 |
| 運算啟動 | Compute → Clusters | 所有 provisioning stages 完成，狀態 Active |
| 登入驗證 | 該 cluster 的 Sign-in URL | 用該組織帳號執行測試 SQL 成功 |
| 旅遊資料 | 私有來源 DB＋Zetaris SQL Workspace | 六個 views 與三個 REST 來源驗證完成 |

操作時先確認 AWS 帳號、區域、角色權限與預算。按官方引導建立必要基礎資源後，再建立適合測試規模的 cluster；不要直接套用網路上另一人的 account ID、IAM role、region 或 JDBC endpoint。從 cluster Overview 取得實際入口與組織憑證，不沿用本機 Docker 密碼。

第一次登入後在 SQL Workspace 執行：

```sql
SELECT 1;
```

可再查該 cluster 隨附的 sample source；範例資料名稱依部署版本不同，不把缺少 sample 當成平台完全不可用。記錄 cluster 名稱、region、stack 名稱、私有網路與 endpoint，憑證另存在你們的秘密管理流程。

官方原始步驟及成本／清理細節請看 [Managed AWS cluster](https://github.com/zetaris/getting-started/blob/main/docs/install/managed-aws-cluster-guide.md)。不要假設開發版等於 AWS 資源永久免費；以帳號實際 credits、用量與當前方案為準。

### B1. 先有服務，再搬資料

1. 在本機執行 `python3 scripts/package_zetaris_migration.py`。
2. 將 ZIP 與 `.sha256` 經私有傳輸方式送到能連 AWS 來源 DB 的匯入機。
3. 按 [README.md](README.md) 驗證、解壓，以 `restore_source.py` 匯入全新的專用 PostgreSQL DB。
4. 在 AWS Zetaris 依序登錄 `register.template.sql`、`weather.sql`、`insights.sql` 中缺少的項目；各來源名稱已存在時先比對，不能盲目重建。
5. 用 `verify.sql` 與 manifest 核對六組資料筆數；天氣、路況、收費各做實際查詢。

AWS Zetaris 的 VPC 必須能連來源 DB；DB 可用私有 RDS PostgreSQL，或你們維護的私有 PostgreSQL。若沒有來源 DB，先建立它，再做匯入。Zetaris 系統 metastore 不能直接拿來放旅遊資料。

容器或運算節點不能透過 `host.docker.internal:55433` 存取你電腦。改用目標 DB 的私有 DNS、唯讀角色與驗證完整的 TLS；不要把 PostgreSQL 公開給整個網際網路。

### B2. 平常如何恢復、暫停與收尾

已有 cluster 時，在 Cloud Gateway 選取正確 cluster，按目前版本提供的恢復／啟動操作，等它回到 Active，再登入測 SQL；不要重新建立第二套 stack 來「重開機」。依團隊需要設定運算排程。

暫時不用時可用 cluster 的 Suspend／排程暫停運算。暫停不等於整套 AWS 資源已刪除；來源 RDS、儲存與其他資源仍要分別管理。若要永久收尾，先備份需要的資料，再依官方流程處理 cluster、CloudFormation 與保留儲存，確認沒有其他使用者共用後才刪除。

### B3. 如果你要的是 EC2 自架 Docker 版

那是另一條部署路徑：需要官方確認該版本映像、授權、架構及運作方式適用 AWS VM，再準備 Docker、磁碟持久化、備份、TLS／私有存取與運維。不能把本機 compose 的 loopback 管理入口直接開到公網，也不要把本機 `.env` 或整個 Docker volume 當成可攜的正式部署包。本專案沒有提供經 AWS 驗證的自架 CloudFormation／Terraform，請勿把這份教學理解為已完成 EC2 正式部署驗收。

## C. 最後才接手機

目前 Zetaris 後端連接器限制 loopback HTTP。AWS 平台啟動和資料搬移完成後，仍需另外實作／設定後端的 HTTPS 或內網服務身分連線、組織權限與目標版本 API；手機只呼叫 App 後端，不持有 Zetaris 管理帳密。這些部署參數不能靠一個本機資料 ZIP 自動推導。

## D. 快速檢查

| 症狀 | 檢查 |
| --- | --- |
| Docker 無法啟動 | Docker daemon、平台資源需求、官方 registry 權限 |
| UI 能開但查詢失敗 | driver／api／bootstrap 狀態、官方 verify、登入組織 |
| 本機能查而 AWS 不能 | VPC／DNS／SG／TLS、DB 角色及目標平台版本 |
| 資料包匯入拒絕重跑 | 使用全新專用來源 DB；不刪正式資料來繞過保護 |
| 天氣／路況／收費失敗 | 引擎對外 HTTPS、來源可用性、REST endpoint，不能以靜態舊值冒充即時資料 |
| 停止後帳單仍有用量 | 分開檢查 compute、DB、EFS／S3、網路與 retained resources |

本機驗證與 AWS 驗證分開記錄；這份專案曾完成本機還原、Zetaris 查詢與手機測試，不代表已取得你 AWS 帳號的部署驗收結果。
