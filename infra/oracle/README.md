# Oracle Always Free 後端

本分支將 ASP.NET Core、Node.js 24（H-MEM）及 PostgreSQL 放到同一台免費 VM。Zetaris 引擎不放在這台小型 VM；已有的雲端 Zetaris 可由後端另行連線。

## 免費資源範圍

2026-10-25 官方 Always Free 文件列出 A1 每月 1500 OCPU 小時、9000 GB 小時，約為全月 2 OCPU／12 GB；boot／block volume 合計 200 GB。僅適用 tenancy 的 home region，仍受剩餘額度與機房容量限制。

本次目標是 Sydney `ap-sydney-1`、`VM.Standard.A1.Flex` 1 OCPU／6 GB、Ubuntu 24.04 Minimal aarch64、50 GB 開機磁碟。只用標示免費資格的映像與規格，不用試用點數代替 Always Free 資格。建立前須確認現有資源及主控台費用；容量不足就停止，不切付費規格。閒置免費 VM 可能被 Oracle 回收。

官方資料：[Always Free resources](https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm)。

## 建立與初始化

1. 在 Oracle 主控台確認 Sydney 是 home region，先核對既有 VM 與磁碟用量。
2. 建立上述免費 VM，加入自己的 SSH 公鑰；私鑰留在個人電腦，不放進 Git。
3. SSH ingress 僅開給測試者來源 IP；不對外開 PostgreSQL 5432 或後端 8787。
4. 可在進階設定上傳 `cloud-init.yaml`，或 SSH 登入後自行安裝 Ubuntu 官方套件 `docker.io`、`docker-compose-v2`，啟用 Docker。

## 快速打包

在專案根目錄執行：

```sh
python3 scripts/package_oracle_backend.py --snapshot /secure/path/sydney-live-source.zip
```

只打包後端、H-MEM 程式、共享公開資料與部署設定。可選擇使用已驗證的 Zetaris 公開資料快照，重建 POI、秘境、OSM 及活動票價檔案。原始個人行程、對話、偏好與資料庫帳密不在包內。天氣維持即時 API。

輸出 `infra/oracle/bundles/*.tar.gz` 與 SHA-256。傳至自己 VM 的 `/opt/sydneybuddy`，核對 checksum 後解開到全新 release 目錄；不要覆蓋既有正式資料目錄。

Ubuntu VM 可用這個工具完成校驗、傳送與啟動；若尚未安裝 Docker，工具會從 Ubuntu 官方套件來源安裝 `docker.io` 與 `docker-compose-v2`：

```sh
python3 scripts/oracle_backend.py deploy --host YOUR_VM_PUBLIC_IP --key /secure/path/id_ed25519 --bundle infra/oracle/bundles/YOUR_RELEASE.tar.gz --dry-run
python3 scripts/oracle_backend.py deploy --host YOUR_VM_PUBLIC_IP --key /secure/path/id_ed25519 --bundle infra/oracle/bundles/YOUR_RELEASE.tar.gz
python3 scripts/oracle_backend.py status --host YOUR_VM_PUBLIC_IP --key /secure/path/id_ed25519
python3 scripts/oracle_backend.py tunnel --host YOUR_VM_PUBLIC_IP --key /secure/path/id_ed25519
```

工具只對指定 VM 操作；初次連線使用 OpenSSH 的首次信任機制記錄 host key，之後拒絕不符的主機金鑰。首次部署在 VM 生成新的私有資料庫密碼，既有 `.env` 不會覆蓋。更新仍保留同一組資料 volume。

## 啟動服務

在 VM 建立 `/opt/sydneybuddy/.env`（權限 `0600`），以 `.env.example` 為範本，將 `POSTGRES_PASSWORD` 改成隨機的長 hex 字串。可用 Python `secrets.token_hex(32)` 產生；不要保留範本密碼。供應商金鑰可留空，相關功能需補上金鑰後才可用。

在 release 的 `infra/oracle/.env` 建立指向私有設定的符號連結，再啟動：

```sh
ln -s /opt/sydneybuddy/.env /opt/sydneybuddy/RELEASE/infra/oracle/.env
sudo docker compose --env-file /opt/sydneybuddy/.env \
  -f /opt/sydneybuddy/RELEASE/infra/oracle/compose.yaml up -d --build --wait
curl --fail http://127.0.0.1:8787/health
```

API 以非 root 帳號執行。PostgreSQL 資料、H-MEM、寵物會話與私有設定分別保存在持久化 volume；重新建立容器不會清除它們。首次為新的應用資料庫，不會自動搬走本機個人資料。服務隨 Docker 自動重啟。

## 從本機／App 測試

現有後端是本機存取架構；本次先透過 SSH 加密通道測試，保留這個限制。另一個終端執行：

```sh
ssh -i /secure/path/id_ed25519 -NT -o ExitOnForwardFailure=yes \
  -o ServerAliveInterval=30 -L 127.0.0.1:8790:127.0.0.1:8787 ubuntu@YOUR_VM_IP
curl --fail http://127.0.0.1:8790/health
```

iOS 模擬器的 `SydneyBuddyBackendURL` 設 `http://127.0.0.1:8790`；Android 設 `BACKEND_URL=http://127.0.0.1:8790` 後重建，並用 `adb reverse tcp:8790 tcp:8790` 連接。每位夥伴使用自己的授權 SSH 金鑰；不可散發部署私鑰。實體手機透過網際網路直連的公開入口，另需 HTTPS 與應用程式登入／資料隔離，不能直接將目前匿名 API 公開。

## Zetaris 切換與自行設定

App 仍可選直接來源、本機 Zetaris、雲端 Zetaris。這裡的「本機 Zetaris」是指 **後端所在 VM** 的 loopback；不會自動穿越網路連回個人電腦。雲端設定可在 VM 私有 `.env` 的 `Zetaris__Cloud__*` 填入，或使用環境變數；範本不包含任何實際帳密。不要把管理帳密塞進 App。

本機保存的 Zetaris 管理設定不會隨這份部署包傳上 Oracle。若需要搬移既有敏感資料或帳密，先確認具體資料與目標，再單獨進行。

## 更新、備份與清理

更新時建立新的 release，先備份資料庫及 app-state，重建後檢查健康與代表性 API。單 VM 更新可能短暫中斷。`docker compose down` 保留 volume；`down -v` 會刪除資料，不能當作日常停機命令。

Oracle 免費 VM 不會消除其他供應商費用；先前 AWS Zetaris 仍依使用者要求保留。是否移除 VM、開機磁碟、備份、網路與 AWS 資源，待團隊測試完成後另行確認。

目前狀態：部署檔已準備；VM 建立、遠端啟動與 App 連線尚待驗證。

2026-10-26 本機驗證：部署包已實際建置為 `aarch64` 映像，API／PostgreSQL 容器皆健康，公開景點搜尋成功；重建 API 容器後合成 H-MEM 資料仍存在。後端 388 項測試通過。此紀錄不代表 Oracle 遠端部署已完成。
