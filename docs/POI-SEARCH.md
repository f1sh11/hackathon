# 開源地點搜尋與景點工具箱

## 使用方式

- iOS / Android：地圖首頁的「搜尋景點、餐廳與設施」，或時間軸左側「地圖找地點」。
- 搜尋頁先移動地圖，再選景點／餐飲／生活設施，輸入名稱或留空，按「搜尋」。搜尋範圍為地圖中心附近 1.5 km，目前限悉尼。
- 地圖標記與結果清單可以互相選取，按「詳細介紹」看說明與 OSM 原始營業時間標記；按「＋工具箱」先收藏，不會直接加入行程。
- 收藏存在裝置本機。開啟行程編輯器時合併進候選工具箱；保存行程後，地點快照保留在既有行程 JSON 中，重新試算不需要再次查詢 OSM。
- 左欄約占三分之一，依推薦排序；按住 ☰ 拖到右欄指定時段。已有的當日景點會移動；其他日期已排的地點會標示日期，也可直接跨日搬動，保留活動與停留長度，避免重複加入。拖到日期上停留約 0.45 秒會切換右側時間軸，再繼續拖到指定時段放開。
- 日期切換位於右側時間軸上方。iOS 與 Android 均保留卡片上下把手、5 分鐘吸附、縮放、詳細編輯與還原。
- OSM 餐飲地點排為用餐，廁所排為廁所停靠，飲水點排為休息，其餘為景點。

## API 與資料來源

`POST /poi/search`：`{lat,lng,radius,category,query}`。`category` 為 `sights` / `food` / `facilities`，範圍限制 100–5000 公尺，名稱最多 80 字。

回應 `{places,source,attribution,message}`；地點使用 `osm:node:id` / `osm:way:id` / `osm:relation:id` 作為穩定識別，way / relation 採中心點。此點未必是實際入口。

- `IPoiProvider` 是可替換來源介面，目前實作 `OsmPoiProvider`，直接讀取 Overpass，不抓取 Google 頁面。
- 可透過伺服器設定 `Poi__OverpassUrl` 改用自己的或適當的公共 Overpass 服務。
- 相同查詢快取 15 分鐘、公共查詢序列執行、不同查詢至少間隔 3 秒，服務錯誤後暫停 30 秒。按鍵觸發，不做逐字自動查詢。
- 公共服務可能忙碌、逾時或無法連線；顯示錯誤供手動重試，不改用示例地點冒充成功結果。
- Overture、Foursquare、Google Places 尚未實作資料供應器；可新增對應供應器與正規化／授權規則。地圖底圖與 POI 來源是分開的，本次搜尋地圖明確使用 OSM。

## 資料可信度

OSM 有值才顯示介紹、名稱與 `opening_hours` 原文。沒有評價、即時營業確認、排隊或價格資訊。未解析完整 `opening_hours` 規則，排程會標示營業時間未確認，不能保證可入場。

OSM 地點費用未知，數值 0 僅表示尚未預留，**不是免費**；工具箱與介紹會顯示「費用未知」，加入行程後會提示預算尚未包含該地點的費用。可在用餐／設施詳細編輯中補規劃金額；不會將外部未驗證價格視為報價。

請保留「© OpenStreetMap contributors · ODbL」標示；資料與公共服務說明：[OSM 授權](https://www.openstreetmap.org/copyright)、[Overpass 標籤查詢](https://dev.overpass-api.de/overpass-doc/en/criteria/per_tag.html)、[公共服務資源規則](https://dev.overpass-api.de/overpass-doc/en/preface/commons.html)。

## 實作位置

- 後端 `Services/PoiService.cs`、`Api/PlanningEndpoints.cs`、`Planning/ScheduleEditorService.cs`。
- iOS `PoiDiscoveryView.swift`、`ScheduleWorkbench.swift`、`ScheduleEditor.swift`。
- Android `PoiDiscovery.kt`、`ScheduleWorkbench.kt`、`ScheduleEditor.kt`、`BuddyStore.kt`。

## 本次本機驗證限制

已以記錄回應驗證 POI 解析、地圖結果、介紹、加入工具箱與排程。本機直接連 Overpass 主站曾收到 HTTP 406／連線失敗；另一公共節點逾時，因此尚未確認這台電腦能成功下載線上 POI。App 會呈現服務錯誤，不顯示假結果。可用 `Poi__OverpassUrl` 指向可連線的 Overpass 部署。

本次回歸驗證：後端 120 項、iOS 23 項、Android 27 項單元測試與 8 項畫面互動測試通過；Android Lint 通過。畫面測試中的 POI 搜尋使用記錄回應，未將其當成線上資料下載成功的證據。
