# 行程 JSON 測試接口

AWS 部署驗證結果記錄於 `outputs/aws-planning-test-20261027/verification.json`；部署完成後可使用下列網址與接口。

基底網址：`https://api.a6c43f1d-2765-487d-89af-99b612b39de9.cloud.zetaris.com/sydneybuddy`。所有雲端請求需帶 `Authorization: Bearer <App連線碼>`；不需要 NVIDIA 或 Zetaris 帳密。

| 方法與路徑 | 用途 |
|---|---|
| `GET /planning/test/example` | 下載 JSON 範例，日期為雪梨明天 |
| `POST /planning/test` | 送入 JSON 或上傳一個 `file` 欄位的 `.json` 檔 |
| `POST /planning/test?download=true` | 將同樣的測試結果作為 JSON 檔下載 |
| `GET /planning/runs/{runId}` | 查詢回傳 runId 的執行紀錄 |

測試和 `/plan` 共用規劃與費用權限規則。回傳 `saved: false`，不建立或修改正式行程；規劃執行紀錄仍依現有機制記錄。檔案內容直接解析，不依上傳檔名讀寫伺服器檔案。最大 JSON 內容為 200,000 bytes，只接受單一 JSON 物件，不接受陣列或整份已生成的行程當成需求。

## 本機使用

以下命令在專案根目錄執行；範例檔為 `examples/planning/request.json`。

```sh
curl --fail-with-body http://127.0.0.1:8787/planning/test/example -o planning-request.json

curl --fail-with-body --max-time 240 \
  -H 'Content-Type: application/json' \
  --data-binary @examples/planning/request.json \
  http://127.0.0.1:8787/planning/test -o planning-result.json

curl --fail-with-body --max-time 240 \
  -F 'file=@examples/planning/request.json;type=application/json' \
  'http://127.0.0.1:8787/planning/test?download=true' -o planning-result.json
```

Postman：選 POST，Body → form-data，新增 `file`、將型別設為 File，選取 JSON；或 Body → raw → JSON 直接貼入範例。不要同時加入其他表單欄位。

## AWS 使用方式

先將既有 App 連線碼放進 `APP_CONNECTION_CODE` 環境變數，勿把它寫進需求 JSON 或提交 Git。以下會回傳可下載的測試結果包裝。

```sh
curl --fail-with-body --max-time 240 \
  -H "Authorization: Bearer $APP_CONNECTION_CODE" \
  -H 'X-Travel-Data-Source: zetaris-cloud' \
  -H 'Content-Type: application/json' \
  --data-binary @examples/planning/request.json \
  'https://api.a6c43f1d-2765-487d-89af-99b612b39de9.cloud.zetaris.com/sydneybuddy/planning/test?download=true' \
  -o planning-result.json
```

雲端檔案上傳：將上面 `Content-Type` 與 `--data-binary` 兩行換成 `-F 'file=@examples/planning/request.json;type=application/json'`，網址不變。原有 `/plan` 仍保留相同輸入契約，回傳草稿本體。

## JSON 欄位

- `date`：旅遊起始日 `YYYY-MM-DD`；靜態範例日期請自行更新。
- `days`：1–15 天。
- `mode`：`demo`／`assisted`／`live`，語意及權限與 App 相同。注意：當伺服器啟用 Meterless Skill Agent 時，現有規劃流程即使使用 `demo` 仍可能呼叫已設定的模型；demo 不保證零外部呼叫。`live` 額外需要既有付費服務授權。
- `transportPreference`：`walk`（步行）、`balanced`（比較公共運輸）、`lessWalking`（盡量少走）。公共運輸需要該後端已配置交通服務。
- `budget`：每日活動預算，住宿預算另計；`maxSteps` 仍受後端硬上限驗證。
- `startMinute`／`endMinute`：當地當日分鐘數，600＝10:00，1200＝20:00。
- `mustVisit`：`GET /catalog` 或已提供規劃候選中的景點 ID，不是自由填寫的名稱。
- 已訂住宿：加入 `hotel: {name,lat,lng}` 與 `accommodationStatus: "booked"`；依實際已訂資料填寫。未選住宿不要假裝已訂。

範例以 `walk` 降低本機交通服務設定需求；要測試公交優先，改為 `balanced`。實際模型測試通常使用 `assisted`，並確認 `/planning/agent/status` 的 provider/configured。

## 回傳與錯誤

```json
{
  "runId": "執行紀錄 ID",
  "saved": false,
  "skillVersion": "2026-10-27.5",
  "elapsedMilliseconds": 1234,
  "draft": {"days": []}
}
```

`draft` 是與 App 相同的草稿契約，仍需檢視與驗證，測試成功不等於已滿足所有偏好或已訂位。

- 400：JSON 格式或根物件不合法。
- 413：內容超出大小限制（整體請求亦受既有 250,000 bytes 上限限制）。
- 415：原始內容未使用 `application/json`，且不是支援的表單上傳。
- 422：欄位、檔名／表單欄位不合法，或規劃限制、交通／模型供應商尚未就緒。格式沿用既有 `{error, runId}`，不暴露金鑰。

## 最快的本機測試工具

工具預設讀取本機已保存的 AWS 私有連線檔，不列印連線碼。

```sh
python3 scripts/test_planning_json.py --cloud --sample planning-request.json
# 編輯下載的 planning-request.json，再上傳並下載結果：
python3 scripts/test_planning_json.py --cloud --upload --input planning-request.json --output planning-result.json
# 直接送 JSON（不走 multipart）也支援：
python3 scripts/test_planning_json.py --cloud --input examples/planning/nature-two-days.json --output nature-result.json
```

若私有設定放在別處，使用 `--connection-file /你的私有設定位置.json`；格式是 `{ "url": "基底網址", "accessCode": "App連線碼" }`。不要把它放進 Git。

可匯入 `examples/planning/postman_collection.json`，在 Collection Variables 填 `app_access_code`，調整 `travel_date`、`days`、`budget`、`transport` 後執行「調整參數並規劃」；更多偏好直接修改 Body JSON。上傳檔案請使用「上傳 JSON 檔案」並選檔。請在 Postman 儲存回傳內容，或使用提供的 Python 工具自動存檔。

範例參數對照：

| 範例 | 天數 | 每日活動預算 | 步數硬上限 | 喜好 |
|---|---:|---:|---:|---|
| `culture-one-day.json` | 1 | AUD 120 | 10,000 | 文化，必去歌劇院 |
| `nature-two-days.json` | 2 | AUD 180 | 15,000 | 自然 |

調整 Skill 的文件與操作方式見 `PLANNING-SKILL-WORKFLOW.md`；不需要把 Skill 混入旅遊需求 JSON。
