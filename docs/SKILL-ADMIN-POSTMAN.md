# Postman 全程管理旅遊規劃 Skill

管理 API 已於 2026-10-27 部署至既有 AWS 後端，獨立管理憑證已設定並保存於本機 Postman Vault。原有 App 連線碼本身沒有管理權限；目前有效 Skill 仍為 2026-10-27.5，內容未改變。測試只上傳 deployment-check-20261027 候選，未啟用。部署臨時權限已撤銷。

## 一次性後端設定

後端設定 `SKILL_ADMIN_TOKEN_FILE` 指向只供後端讀取的私有文字檔，或設定 `SKILL_ADMIN_TOKEN` 環境變數。內容至少 32 字元，建議以密碼學亂數產生 32 bytes 以上的值。未設定、過短或檔案不可讀時，一律拒絕管理 API。

AWS 沿用既有 HTTPS gateway；需同時提供：

- `Authorization: Bearer <現有 App 連線碼>`（由 gateway 驗證）
- `X-Skill-Admin-Token: <獨立 Skill 管理憑證>`（由後端驗證）

不要把管理憑證放進 App、Git、共用 Collection 或聊天。Postman 使用本機 Vault 的 `sydneybuddy-skill-admin-token`。現有 App code 仍放 Vault 的 `sydneybuddy-app-access-code`。

目前使用單一後端 instance + 持久磁碟；更新集中由這組 API 操作，不要同時用舊 kubectl 腳本寫入 active.json。若未來擴為多個 replica，需先改共用資料庫鎖與一致性機制。

## 匯入與網址

匯入 `examples/postman/SkillAdmin.postman_collection.json`，它是獨立 Collection，不覆蓋原來的 API 請求或自訂旅遊參數。

Collection Variables 的 `skill_admin_base_url` 已設定為既有 AWS 後端網址（結尾包含 `/sydneybuddy`）。需要本機測試時可改為 `http://127.0.0.1:8787`，並另行設定本機管理憑證。這個變數獨立於原 Collection。

## 方式 A：直接上傳中英文 Markdown（建議）

1. 執行 `01 管理狀態與目前版本指紋`，確認目前版本。
2. 以 `08`、`09` 下載目前完整中文、英文 Skill：Send 旁選 `Send and Download`。
3. 在兩個 Markdown 文件尾端追加規則，不刪改既有文字。保留 UTF-8 編碼；內建基底必須逐字保留。
4. Collection Variables 設定新 `skill_version`，例如 `2026-10-27.6`。
5. 執行 `04c 上傳完整中英文 Markdown`，Body 選 form-data：

| Key | Type | Value |
| --- | --- | --- |
| version | Text | 新版本號 |
| baseVersion | Text | 2026-10-27.5 |
| en | File | 英文完整 Markdown |
| zhHant | File | 中文完整 Markdown |

每檔最多 110000 bytes；萃出的每語言追加內容上限 64000 UTF-8 bytes，bundle 上限 150000 bytes。上傳只建立候選版，不啟用。同版本內容不可覆寫；修訂請換新版本號。

6. 執行 `05 預覽候選版本`。檢查 `appendOnly=true`、`activeValid=true`、`removedRules` 為空。此步會擷取最新 `active_fingerprint`。
7. 確認新規則內容後，把 Collection Variables 的 `allow_writes` 改成 `yes`，單獨送出 `06 啟用候選版本`。
8. 讀取公開 `GET /planning/skills?language=zh-Hant`，確認新 version、content 與 sha256，再用原有規劃測試接口比較結果；回傳的 `skillVersion` 應一致。
9. 把 `allow_writes` 改回 `no`。不要對整組 Collection 跑 Runner。

下一個 HTTP 請求使用新 Skill；進行中的規劃保留原快照。不重啟後端，也不自動改寫已保存行程。Skill 文字不會取代營業時間、預算、交通與步數等程式驗證。

## 方式 B：JSON 檔／直接貼 JSON

先下載 `03` 的 JSON 範本。它包含目前累積的追加規則；保留原有 appendices，再加上新規則並換版本。初次啟用時，兩個語言的 appendix 都不能留空。

```json
{
  "version": "2026-10-27.6",
  "baseVersion": "2026-10-27.5",
  "enAppendix": "Retain previously appended rules here.\nAppend new rules below.",
  "zhHantAppendix": "保留先前追加的規則。\n在底部加入新規則。"
}
```

不要把「保留原規則」這句描述當作原規則的替代內容；真的需要包含原有全文。欄位是追加內容，不要再貼一次內建完整 Skill。

- `04`：Body → form-data → file 選擇 JSON。
- `04b`：Body → raw → JSON，直接貼範本修改。
- 接續步驟與 Markdown 相同：`05` 預覽 → `06` 啟用 → 公開接口確認 → 規劃測試。

## 回復與移除規則

一般啟用只能保留現有追加文字並繼續新增，不能移除或改寫舊規則。

回復前，把 `skill_version` 設為目標舊版，執行 `05`；回應 `removedRules` 列出將被移除的文字。如果不是單純移除尾端，會列出目前的完整 appendix 供比較。先與使用者討論，再明確決定是否回復。

若確定回復：

- `rollback_version` 設為目標版本（`2026-10-27.5` 代表內建基底）。
- `confirm_removal` 設為 `true`。
- `allow_writes` 設為 `yes`。
- 單獨送出 `07`；不要因 409 就直接刷新重送，必須重新檢視差異。

啟用前會備份原始 active 檔案；舊版與候選版保留在持久磁碟。若 active 檔案損壞，公開規劃仍會報錯，管理狀態與回復接口仍可使用；管理者需審閱有效目標後明確回復。

## 接口一覽

| 方法 | 路徑（相對後端 base URL） | 用途 |
| --- | --- | --- |
| GET | /admin/planning/skills/status | 有效版本、有效性與併發指紋 |
| GET | /admin/planning/skills/versions | 版本清單 |
| GET | /admin/planning/skills/template | 下載目前累積追加內容 JSON |
| POST | /admin/planning/skills/versions | 上傳候選 JSON（raw 或 multipart file）並驗證 |
| POST | /admin/planning/skills/documents | 上傳完整中英文 Markdown 並驗證 |
| GET | /admin/planning/skills/versions/{version} | 預覽內容、保留檢查與移除差異 |
| POST | /admin/planning/skills/versions/{version}/activate | 只允許追加的啟用 |
| POST | /admin/planning/skills/rollback | 明確確認後回復舊版 |

啟用 Body：

```json
{"expectedActiveFingerprint":"從 status 或預覽取得的 activeFingerprint"}
```

回復 Body：

```json
{"version":"目標舊版本","expectedActiveFingerprint":"剛確認的指紋","confirmRemoval":true}
```

401 表示缺少／錯誤的 App 或管理憑證；409 表示版本已被他人改動、或同名版本內容不同；422 表示規則、格式、確認條件或基底不符。JSON 語法錯誤為 400，不支援媒體類型為 415，超過上傳限制可能為 400／413。正常 API 不會回傳管理憑證。
