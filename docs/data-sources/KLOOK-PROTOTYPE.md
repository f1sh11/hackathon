# Klook 活動資料讀取原型

這是獨立實作的公開頁 JSON-LD 讀取器，沒有 Apify Actor 的原始碼，也沒有呼叫付費 Actor。不是正式 Klook API，尚未接到手機搜尋或 Zetaris 正式資料表。

## 公開參考與差異

參考：[Apify zinin/klook-scraper](https://apify.com/zinin/klook-scraper)、[輸入規格](https://apify.com/zinin/klook-scraper/input-schema)。該 Actor 宣稱從搜尋頁擷取商品卡，提供名稱、價格、幣別、評分、評論數及預訂量等欄位；文件描述使用住宅代理與 anti-detect browser。本專案沒有採用這些機制，也不宣稱重現其內部實作。

[Klook robots](https://www.klook.com/robots.txt) 的通用規則限制搜尋頁。原型僅接受手動提供的 canonical 活動網址，讀取 robots 後再請求；不遍歷搜尋、不自動發現活動、不讀私有 API、不登入、不解驗證、不下載圖片。

## 使用方式

在專案根目錄執行，僅需 Python 標準函式庫：

```sh
python3 scripts/import_klook_offers.py \
  --url 'https://www.klook.com/en-AU/activity/58684-sydney-opera-house-architectural-tour-sydney/' \
  --output infra/poi/data/klook-probe.json
```

一次最多五個網址，逐筆請求間隔兩秒。收到 401、403、429 或辨識到驗證頁即停止整批；不自動重試。禁止重新導向。單頁上限 5 MiB、請求逾時 25 秒、TLS 正常驗證。輸出位於已忽略的本機資料目錄，不提交網頁內容。

若有合法取得的已儲存 HTML，可測試離線解析：

```sh
python3 scripts/import_klook_offers.py \
  --url 'https://www.klook.com/en-AU/activity/123-example/' \
  --html /path/to/saved-page.html \
  --output infra/poi/data/klook-offline.json
```

離線模式不發送請求；範例網址只是格式示範，不代表真實商品。只解析公開結構化 Product / TouristTrip / TouristAttraction 物件，不猜測私有嵌入狀態。

輸出狀態：`ok`、`blocked`、`robots_disallowed`、`unsupported_page`、`http_error`、`fetch_failed`。任何非成功狀態結束碼為 2；空資料不能視為成功。`unsupported_page` 表示沒有支援的結構化資料，不表示網站沒有活動。

## 資料語意與後續整合

- `prices` 只收明確數字與 ISO 幣別，不從 en-AU 推測 AUD；缺少者為空。
- `advertised_from_price` 是頁面參考起價，`budgetEligible=false`。日期、票種、成人／兒童、套餐與費用都尚未選定，不能直接當成每人預算，也不是菜單價或網友單次消費。
- `rating` 附 `ratingScale`，`reviewCount` 是摘要數量，沒有匯入評論全文、作者、照片或推薦菜色。
- `coordinates=null`，商品名稱不等於地點，不能憑名稱捏造座標。
- 每筆保留來源網址、擷取時間；授權狀態明確未知，公開可讀不代表開源或可任意重用。
- 預訂量即使以後取得也只能保留原始累積標籤，不能換算即時人潮。

取得穩定且可使用的資料來源後，可採用 `ASP.NET Core 活動匯入／確認地點 → PostgreSQL travel_offers → Zetaris 活動查詢 view → App 景點詳情的外部票券`。應與現有餐廳 evidence、行程預算保持獨立；指定日期／票種／幣別後才建立新的預算估算版本。現在沒有建立空的正式資料表或把測試資料加入使用者搜尋結果。

## 實測與驗證（2026-10-25）

從本機一般 HTTP 讀取上方雪梨歌劇院活動頁，回傳 HTTP 403 與 CAPTCHA 驗證頁；執行原型結果為 `blocked`、商品數 0、結束碼 2。沒有繞過驗證，也沒有把搜尋引擎快取或 Actor 範例當成實測資料。

```sh
python3 -m unittest discover -s scripts/tests -p test_klook_offers.py
```

測試使用明確的合成 fixture，驗證價格／幣別、來源、未知座標、阻擋與不支援狀態、網址限制及非法價格。即使離線測試通過，也不表示即時網站抓取已打通。正式串接可改用取得存取權的 Klook 合作服務，或經確認可用的授權匯出；兩者均需要另外設定來源，這版沒有購買或啟用第三方付費服務。
