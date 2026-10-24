# 地圖的交通、人潮與費用資訊

此版在 `feat/zetaris-local-data` 實作，保留裝置的「直接來源／透過 Zetaris」選擇。iOS 的探索地圖及行程地圖、Android 的地圖及搜尋頁，都能開啟「交通・人潮・費用」資訊；已取得的道路通報與附近參考收費可呈現為地圖標記。

## 實際資料路徑

```text
iOS / Android → POST /data/insights
  direct  → 固定公開來源
  zetaris → 固定 SQL → sydneybuddy_live.traffic_nsw → Live Traffic NSW
                   → sydneybuddy_live.fees_sydney → OSM Overpass
  可選補充 → Google Places（後端直連、獨立標示，非冒充 Zetaris 回傳）
```

- **道路通報**：免金鑰 NSW 公開 incident-open GeoJSON，依搜尋／行程地點附近 2.5 km 過濾；忽略已結束通報。這不是即時車速或壅塞 ETA。保留來源事件 `lastUpdated`，查詢時間不能當成事件發生／更新時間。長期通報可能很久沒修改。
- **費用**：Overpass 只查雪梨 bbox `151.18,-33.90,151.25,-33.82` 的 `charge` 標註，上限 500。與行程地點配對須名稱一致且距離不超過 50 m；附近收費標記限景點、休閒、歷史與餐飲類型。保留原始幣別／單位文字，不將每小時費用當成人均餐費，也不把未知換成 0。來源涵蓋有限，不能保證每家餐廳都有菜單。
- **人潮**：目前沒有可靠的人流來源，`people`／`waitMinutes` 為 null，狀態 unknown。沒有用隨機值、景點分類或 Google 評論數捏造人數與排隊時間。Apple／Google 官方 Places API 沒有可直接補上的 Popular Times。未來接實測／商業服務時應新增獨立 provider，保留觀測／預測時間與可信度。
- **目前與未來**：道路通報指現在，不宣稱是所選旅遊日期的預測；這版沒有自動依通報重算路線或扣排隊時間，也沒有把參考價自動算進預算。

## 啟動與登錄

先讓真正本機 Zetaris 啟動，沿用既有 `sydneybuddy_live`：

```sh
python3 scripts/configure_zetaris_insights.py \
  --platform-env /Users/linziqiao/zetaris-local/zetaris-platform/.env --register
```

工具只接受固定本機登入／SQL API、固定公開來源，既有表不覆蓋。成功會顯示來源集合筆數，不輸出帳密或原始 SQL 錯誤。若表已存在但來源需要變更，先在 Gateway 比對 endpoint，再由管理者更新；工具不會刪表重建。失敗來源可用 `--verify-kind traffic` 或 `--verify-kind prices` 單獨確認。

後端沿用 `Zetaris__EnvironmentFile` 啟動方式。手機設定選「透過 Zetaris」。費用公開結果在後端快取 10 分鐘、道路通報 30 秒，以減少公共服務負載；快取依 direct／zetaris 隔離。天氣沿用每次重新查詢、不快取的設計。HTTP 回應 no-store，查詢時間與來源資料抓取時間分開。

## API 契約

```http
POST /data/insights
X-Travel-Data-Source: zetaris
Content-Type: application/json
```

```json
{"date":"2026-10-24","mapProvider":"apple","places":[{"id":"start","name":"Circular Quay","lat":-33.8611,"lng":151.2126}]}
```

一次最多 30 個地點，驗證座標及名稱長度；客戶端不能傳 URL／SQL。回應包含：

| 欄位 | 意義 |
| --- | --- |
| `dataSource` | 裝置選擇的主要資料路徑 direct／zetaris |
| `fetchedAt` / `scope` | 本次查詢時間與適用範圍說明 |
| `traffic.status/items/fetchedAt` | 路況來源可用性、標記、該資料實際抓取時間 |
| `pricePlaces` | 附近景點／餐飲等的原始參考收費標記 |
| `places[].price` | 配對到各地點的收費；未知 amount=null，不等於免費 |
| `places[].crowd` | 人潮／排隊目前為 unknown、數值 null |
| `places[].googleFallback` | 補充來源未啟用、缺金鑰、請求上限等狀態 |
| `places[].price.retrievalPath` | 使用 Google 補充時為 google-direct-supplement；不宣稱經過 Zetaris |
| `priceDataFetchedAt` | OSM 費用資料實際抓取時間 |
| `attribution` | 公開來源署名 |

各類來源可部分失敗。Zetaris 失敗不會偷偷重查直接 OSM／NSW 來源；畫面顯示不可用。只有符合下方條件的 Google 價格補充是明確允許的例外。

## Google 價格補充與 Apple 限制

只有免費費用未配對、選 Google 底圖、後端 `AllowPaidServices=true` 且已設定 `GOOGLE_SERVER_KEY`，才會呼叫 Google Places。正常測試版預設禁用付費服務，沒有金鑰就不發出請求。

已知 Google place ID 用 Place Details；開放地點用 Text Search，必須名稱正規化後完全一致且距離 ≤50 m 才採用價格。每次 API 請求最多補充 5 個地點，每個 Google 呼叫最多 8 秒。回傳 priceLevel／priceRange 與署名，絕不固定換算成 A$ 平均值，不把 Google 結果存進公開 CSV、Zetaris 資料包或人潮訓練集。

Google 的這條補充路徑由 App 後端直接呼叫，不是 Zetaris 原生 REST 表。Google 回傳只在 Google 地圖情境展示；既有 Apple／OSM 畫面不混入 Google Places 價格。Apple 底圖仍能顯示開放路況與收費，但 MapKit 沒有本功能可用的通用消費價位／熱門時段接口，因此不會加入一個看似能查、實際沒有資料的 Apple 替代呼叫。

目前未設定 Google、TfNSW 金鑰，付費政策為關閉。官方交通 Trip Planner／GTFS-RT 的設定仍沿用既有導航系統；這份事故通報不能代替公車延誤、Metro／Train 到站時間。Google 付費補充只能用 mock 測試驗證，沒有進行實際計費呼叫。

## 對應檔案

- `shared/travel-insight-sources.json`：固定公開來源、範圍與表名。
- `scripts/configure_zetaris_insights.py`：本機原生 REST 表登錄與實查。
- `server-dotnet/SydneyBuddy.Api/Services/TravelInsightService.cs`：驗證、過濾、配對、快取、Google 補充及明確未知狀態。
- `TravelDataService.cs`：固定 Zetaris 查詢，不接受外來 SQL。
- iOS `Models.swift`、`MapView.swift`、`PoiDiscoveryView.swift`：請求狀態、地圖標記與資訊頁。
- Android `BuddyStore.kt`、`IosStyle.kt`、`RouteMap.kt`、`MapProvider.kt`、`PoiDiscovery.kt`：同等介面與標記。
- `migration/insights.sql`：AWS 目標環境的公開來源登錄材料，已納入新的搬移包。

來源：[NSW 官方說明](https://www.livetraffic.com/help)、[Overpass](https://wiki.openstreetmap.org/wiki/Overpass_API)、[Google 欄位](https://developers.google.com/maps/documentation/places/web-service/data-fields)、[Google 使用政策](https://developers.google.com/maps/documentation/places/web-service/policies)。
