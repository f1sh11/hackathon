SydneyBuddy Postman 使用方式

1. 匯入 SydneyBuddy.postman_collection.json。另可匯入 AWS / Local 環境檔；預設 Collection 已使用 AWS。
2. Postman Vault 建立 sydneybuddy-app-access-code，值填 App 連線碼。Collection 的 Bearer Token 直接引用 {{vault:sydneybuddy-app-access-code}}。
   服務供應商密鑰與 Zetaris 管理帳密不需要提供。不要把 App 連線碼存到共用 Collection。
3. 開啟「01 連線與資料狀態 → 健康檢查」，按 Send。
4. 開啟「02 行程 JSON 測試 → 不同參數產生行程」。Collection Variables 可改：
   travel_date、days、budget（AUD 每人每日活動預算）、max_steps、language（en / zh-Hant）、transport（walk / balanced / lessWalking）。
   其他偏好、已訂房位置、必去清單等直接編輯 Body；mustVisit 使用 /catalog 或候選清單中的真實 ID。
5. 上傳 JSON：選「上傳 JSON 檔並下載結果」→ Body / form-data / file，選擇 JSON 檔（200000 bytes 內）。
   Send 旁選單 → Send and Download 保存結果。一般 JSON 請求也提供下載版本。
6. 成功規劃後會自動擷取 draft_json、run_id，可接續診斷、審查與儲存；不要把包含個人資訊的變數匯出分享。
7. 寫入或刪除請求標示 ⚠，預設 allow_writes=no 會擋下。確定測試對象後改 yes，逐一送出；測完改回 no。
   請勿對整個 Collection 執行 Runner。規劃、審查、對話、社群萃取可能使用雲端模型額度。
8. 使用既有行程的接口需自行填 trip_id 與最新 trip_revision；版本衝突回 409 時，重新讀取後再決定。
   其餘空白變數由前一步自動擷取或依請求說明填寫，不會隨機選夥伴資料。
9. 本機切換：選 Local 環境，或 base_url=http://127.0.0.1:8787 且 data_source=local。
   若本機不需驗證，可將該請求 Authorization 改為 No Auth。雲端資料來源固定為 zetaris-cloud。
10. 最後一組 /setup 僅本機可用，AWS 會回 404；服務密鑰建議用本機瀏覽器設定頁填寫。

59 個請求，涵蓋程式目前全部 55 個路由（另提供 JSON 上傳/下載與 Skill 中英文變體）。
Collection 建立完成不等於所有外部服務均可用；來源沒有資料、營業資訊未知、缺少道路證據等仍會如實回傳。
