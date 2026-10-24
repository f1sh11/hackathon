# 地點搜尋與景點工具箱

## 現行來源切換（2026-10-24）

| 底圖 | 地點搜尋 | 查詢時機 |
|---|---|---|
| OpenStreetMap | 本機 `/poi/search` → Overpass | 開啟／換分類自動搜尋，移動地圖後手動搜尋 |
| Apple Maps（iOS） | 裝置上的 `MKLocalSearch`／`MKLocalPointsOfInterestRequest` | 開啟／換分類自動搜尋，直接向 Apple 查詢，不走 OSM |
| Google Maps | 本機 `/poi/search` 的 `provider=google` → Google Places Nearby／Text Search | 切換時清除舊結果，按搜尋後才查詢 |
| 離線示意 | 不提供商家搜尋 | 不呼叫任何地點服務 |

- 切換來源取消舊請求，清掉標記、清單及詳情；過期回應不能覆蓋新來源。畫面與頁尾同步標示資料來源。Google 回傳非 Google 資料時顯示來源錯誤，不回退至 OSM。
- Apple 不需本機後端即可搜尋；可將地點加入工具箱及本機時間軸。iOS 18 以上以 Apple place identifier 產生穩定 ID，舊版以名稱及座標識別。附近搜尋範圍為 1.5 km；餐飲、景點使用 POI 分類，設施使用文字搜尋，不保證公共廁所或飲水設施已驗證。
- Apple／OSM 地點的費用、排隊、室內外與營業狀態維持未確認，不會將零費用當成已知免費或宣稱全天營業。
- Google 沿用伺服器 `GOOGLE_SERVER_KEY` 與 `AllowPaidServices`。選用 Google 底圖仍需各平台地圖金鑰。沒有設定或禁止付費時清楚回報，不自動啟用付費。
- Google 使用明確欄位遮罩、附近半徑和分類；文字搜尋結果另外檢查實際距離。結果不使用 OSM 快取，不永久儲存，也不匯入僅支援 demo 的本機時間軸。切換底圖後不保留 Google 結果或標記；顯示 Google Maps 及第三方來源歸屬。
- Android 沒有 Apple Maps 底圖選項，支援 OSM／Google／離線三種對應行為。儲存行程內的 Apple 地點仍可作為未知費用的匯入地點讀取。

官方介面：[Apple 地點搜尋](https://developer.apple.com/documentation/mapkit/mklocalsearch/request)、[Google Nearby Search](https://developers.google.com/maps/documentation/places/web-service/nearby-search)、[Google Places 來源與儲存規則](https://developers.google.com/maps/documentation/places/web-service/policies)。

驗證：後端 124 項、iOS 42 項、Android 27 項單元測試及 20 項畫面互動測試通過，Android Lint 通過。包含依底圖選擇來源、Google 手動查詢／伺服器付費保護／不使用 OSM 快取、分類與附近距離、過期結果取消，以及 Apple 地點匯入／排程／未知費用與營業時間處理。

iOS 模擬器已用真實 Apple 服務取得附近景點與餐飲，包含 Aurora Place Lobby Cafe、Toca 等，並確認介紹頁為 Apple Maps 來源。未使用 Google 金鑰或發出付費測試請求；Google 為契約／模擬回應測試。OSM 公開 Overpass 連線限制仍在，未宣稱已修復公共服務。

以下為先前 OSM 版本的開發與驗證紀錄，來源固定／不重查的說明已由上方現行行為取代。


## 使用方式

- iOS / Android：地圖首頁的「搜尋景點、餐廳與設施」，或時間軸左側「地圖找地點」。
- 搜尋頁開啟時自動搜尋，切換景點／餐飲／生活設施也會自動查詢；輸入名稱或移動地圖後，按「搜尋」。搜尋範圍為地圖中心附近 1.5 km，目前限悉尼。
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
- 相同查詢快取 15 分鐘、公共查詢序列執行、不同查詢至少間隔 3 秒，服務錯誤後暫停 30 秒。開啟頁面、切換分類或按鍵觸發，不做逐字／移動地圖時的連續查詢。
- 公共服務可能忙碌、逾時或無法連線；顯示錯誤供手動重試，不改用示例地點冒充成功結果。
- Overture、Foursquare、Google Places 尚未實作資料供應器；可新增對應供應器與正規化／授權規則。地圖底圖與 POI 來源是分開的；搜尋頁沿用全 App 的底圖設定，POI 仍來自 OSM / Overpass。

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


## 空白清單修正（2026-10-24）

舊版沒有在開啟頁面或切換分類時發出查詢，初次畫面只顯示操作說明；地圖底圖不代表已取得 POI。新版兩平台自動執行首次／分類查詢，載入中、查詢成功但沒有結果、服務失敗分開顯示；服務錯誤提供重新搜尋，不再把錯誤當成「沒有符合的地點」。切換分類會取消舊請求，避免過期回應覆蓋新結果。

再次實測本機後端回報無法連線 Overpass，直接查詢主站仍為 HTTP 406，另一公開節點逾時。這是尚未解除的線上資料連線限制，沒有宣稱已下載真實 POI。iOS 模擬器已驗證開啟搜尋頁會發出查詢及顯示實際錯誤。

本次修正驗證：iOS 30 項測試、Android 27 項單元測試與 12 項畫面互動測試通過，Android Lint 通過。新測試涵蓋自動查詢／分類變更、錯誤與空結果區別、重試與取消舊請求；成功回應使用注入資料，未冒充線上下載成功。


## 搜尋頁同步底圖（2026-10-24）

- iOS 搜尋頁讀取共用 `AppStore.mapProvider`，Apple Maps 直接顯示 MapKit 原生底圖，OSM 才加上 OSM 圖磚。頁內底圖選單也會同步首頁／其他地圖頁。
- Apple / OSM 互換沿用同一個 MapKit 畫面，移除或加入圖磚圖層，保留搜尋中心、縮放與標記。Google Maps 使用獨立供應器，鏡頭中心與縮放回傳共用搜尋畫面狀態。
- Android 同步共用底圖設定，支援既有 OSM／Google／離線模式。Google 未設定金鑰時顯示提示；離線模式顯示不提供街道底圖的說明，不會默默換回 OSM。
- 切換底圖本身不重新查 POI；輸入搜尋、切換分類的行為維持不變。畫面標明「地點資料：OpenStreetMap」，切 Apple / Google 底圖並未改串它們的 Places 服務。先前 Overpass 連線限制仍在。

驗證：iOS 33 項測試通過，包含實際 MapKit 加／移 OSM 圖層、保留地點標記及鏡頭位置；Android 27 項單元測試、15 項畫面互動測試與 Lint 通過，包含共用供應器切換、未配置 Google 提示與不重查 POI。iOS 模擬器已實際切換 Apple 底圖，並確認關閉搜尋回首頁、再次開啟後仍為 Apple Maps。Google SDK 編譯通過，但本次沒有驗證具有效金鑰的線上 Google 圖磚。
