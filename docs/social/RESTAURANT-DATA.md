# 餐廳價格、評論與行程預算

資料路徑：官網內容／授權 JSON／使用者匯入 → ASP.NET Core 解析與分店比對 → PostgreSQL → Zetaris view → App。

手機入口：**我的 → 匯入菜單與餐廳資料**，貼上 JSON，搜尋地圖地點，選擇正確分店後匯入。App 地點介紹顯示「菜單標價」「網友單次消費」「每人預估預算」「推薦菜色與評論」。沒有菜單或原文日期時顯示未提供，不生成星等或假日期。未結構化的社群花費只顯示原文。

## 已實作範圍

- `POST /restaurants/parse-menu`：`{content,channel}`，解析貼上的官網 HTML `application/ld+json`，或 JSON-LD；支援 Menu／MenuSection 裡的 MenuItem、單一 Offer 的 price／priceCurrency。只回傳預覽，不下載網址、不儲存、不作預算。
- `POST /restaurants/import`：來源及菜單／評論 JSON，加上從地圖選定的地點 ID、名稱和座標；可用 `content` 自動先解析菜單。分店對不到或不是餐飲類別會拒絕。
- `POST /restaurants/estimate`：以餐廳 ID、菜單項目 ID、份數和人數產生有來源的每人估算。
- `GET /restaurants/published`：整理好的餐廳 POI，供公開來源庫同步及查詢。直接 OSM 搜尋立即可見；Zetaris 模式需同步。
- 原本社群匯入的餐廳原文推薦也會進入相同展示；可選 `author`、`postedAt`，缺少時顯示未知，不把匯入時間當發文日期。

這版**沒有自動掃描官網、下載 PDF／圖片菜單、影片 OCR、全網評論抓取、匯率轉換或 Open Price Engine 金鑰連線**。官網 HTML 是由使用者提供；標記為 official_menu 代表來源的宣告，系統未認證網站所有權或即時價格。CSV 需先轉成下述 JSON；只有 HTML 中的結構化菜單會被自動解析。

## 匯入 JSON

以下只說明格式，不是實際店家資料。替換來源與實際數值；手機會補上地圖選取欄位。

```json
{
  "sourceType": "official_menu",
  "sourceUrl": "https://YOUR_RESTAURANT_HOST/menu",
  "sourceName": "餐廳官網菜單",
  "author": "來源作者或店家名稱",
  "observedAt": "2026-10-25T00:00:00Z",
  "license": "填寫實際重用授權或使用依據",
  "menuItems": [
    {"name":"實際菜名","amount":19.95,"currency":"AUD","channel":"dine_in"}
  ],
  "sourceText": "貼上評論或花費的實際原文",
  "reportedSpends": [],
  "recommendations": [],
  "reviews": []
}
```

`sourceType` 可為 `official_menu|licensed_dataset|user_import`。`user_import` 不能把單次消費當成菜單標價。`observedAt` 是來源取得／觀測時間，請填實際日期，不接受未來日期。

菜單的 `channel` 為 `dine_in|delivery|takeaway`，價格單位固定為一項餐點，不是每人。若官網是 JSON-LD，將完整 HTML／JSON-LD 字串放在 `content`，可以省略 `menuItems`；沒有結構化菜單時會回報無資料，不猜測菜名與金額。

單次消費格式：

```json
{"amount":40,"currency":"AUD","people":2,"quote":"原文中包含 40 的實際引句","author":"作者名稱","date":"2026-10-24T00:00:00Z","includes":"原文提到的包含項目"}
```

推薦菜色格式：`{dish,quote,author,date}`；評論格式：`{quote,author,date}`。quote 必須能在 `sourceText` 中逐字找到，推薦菜名也要出現在 quote；date 可以省略，people 不知道時可省略，不推測作者人數。單次消費金額也必須出現在原文。原文證據檢查不代表網友說法已被獨立驗證。

直接呼叫 API 時需再加 `placeId`、`placeName`、`lat`、`lng`，採用 `/poi/search` 回傳的實際地點。搜尋比對範圍 500 米，不按名稱猜經緯度；同一來源中同名但不同 ID 的相鄰分店也不合併。不同供應商之間的近距離合併仍沿用現有地圖匹配，匯入前需確認分店地址。

## 選餐與預算

```json
{
  "placeId":"餐廳搜尋結果的 restaurant:... ID",
  "people":2,
  "currency":"AUD",
  "channel":"dine_in",
  "items":[{"id":"menu:...","quantity":2}]
}
```

金額以 decimal 計算，餐點總額除以用餐人數，每人估算進位至分。只計算所選餐點，沒有提供的飲料、附加費、小費不包含。幣別及內用／外送渠道不可混算；菜單超過 90 天會要求更新。App 目前提供 AUD 內用選餐，其他價格仍可展示。規劃器也只將 AUD 內用估算用於目前 AUD 行程。

每筆估算有不可變的 `estimate:...` ID，活動用 `restaurantEstimateId` 引用。重新估算同一家餐廳不會改掉舊行程；將餐廳重新加入行程會使用新選餐估算。前端自己修改單價或每人金額不會被採信，後端從資料庫讀取原估算並確認餐廳一致。估算過期時會警示、要求重新估算，不以過期證據冒充新價格。無估算的用餐保留既有使用者預留餐費機制，並標示費用與營業資訊未知。

## PostgreSQL 與來源保留

新增三張表：

- `restaurant_imports`：不可覆寫的來源批次、菜單、原文花費與評論證據，依實際分店 ID 分組。
- `restaurant_venues`：整合後的餐廳 POI 與目前可用菜單、最新選餐估算。
- `restaurant_estimates`：不可變的選餐、人數、每人估算、單價及來源版本。

菜單歷史放在 `restaurantData.priceObservations`；同菜名／渠道／來源的目前展示採較新資料。新資料匯入後會將最新選餐估算設回未知，避免舊菜單選擇自動套進新價格；既有行程自己的估算版本仍保留，超過有效期會提示。

來源的原文評論不會轉換成店家星等。每個推薦保留作者、原文連結、發文日期（如有）與觀測時間。照片仍沿用原本個別授權流程。

## 同步真正 Zetaris

啟動新版 App API，套用 migration 後：

```sh
python3 scripts/sync_social_zetaris.py --restaurants --register \
  --platform-env /Users/linziqiao/zetaris-local/zetaris-platform/.env
```

在既有專用公開來源資料庫建立 `restaurant_place_records`／唯讀 `travel_restaurant_places` view，透過 `travel_reader` 註冊為 `sydneybuddy_travel.travel_restaurant_places`。不開放整個 App 使用者資料庫。會核對同步後的 ID，零筆資料也是正常狀態，不建立假餐廳。

啟動後端時加：

```sh
Zetaris__RestaurantsEnabled=true Zetaris__SocialEnabled=true \
Zetaris__EnvironmentFile=/Users/linziqiao/zetaris-local/zetaris-platform/.env \
  dotnet run --project server-dotnet/SydneyBuddy.Api
```

預設 `RestaurantsEnabled=false`，既有 Zetaris 部署未註冊新表時仍能使用。選餐或匯入後要重新執行同步才反映到 Zetaris 搜尋；最多還有 15 秒快照快取。行程的選餐估算引用由 App DB 驗證，Zetaris 保存供搜尋展示的快照。天氣既有不暫存流程保持獨立。

AWS 備份除原本一般資料包外，要包含 App DB 上述三張表，以及公開來源 DB 的 `restaurant_place_records` 和 view／唯讀授權。一般資料打包腳本目前不會自動包含這些新增表，不能只搬舊包。不要把包含評論、來源或使用者選餐的 DB 備份提交 Git。
