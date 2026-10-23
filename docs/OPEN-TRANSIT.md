# 開放交通資料與多模式路線

本地開發分支：`feat/itinerary-map-overview`。

**目前完成的是服務串接、雙平台介面與本機測試。尚未匯入 NSW 真實 GTFS／啟動 OTP，所以真實公車班次尚未啟用。** App 不會在查不到資料時產生假的公車號碼。測試圖中的 `TEST-01` 只存在於測試 fixture，畫面明確標示非真實班次；正式程式不提供 fixture 選單或自動退回測試資料。

## 架構

```text
底圖設定 → 預選路線來源（手動按「規劃交通路線」才查詢）
  ├─ OpenStreetMap → 開源 Valhalla / OTP
  ├─ Google Maps → Google Routes（服務端付費開關 + 金鑰）
  └─ Apple Maps → iOS MKDirections（步行 / 開車）
                       公交：明確選 NSW / 開源，或交給 Apple Maps 查詢

SwiftUI / Jetpack Compose
  ├─ Apple → 裝置端 MKDirections → 共用 JourneyPlan
  └─ POST /journey/plan
       └─ JourneyService：驗證來源、逐段時序、停留時間
            ├─ open → Valhalla（道路）/ OpenTripPlanner 2.10（GTFS 公交）
            ├─ nsw → TfNSW Trip Planner API（公交）+ Valhalla（獨立道路段）
            └─ google → Google Routes（道路 / 公交）
       → JourneyPlan / legs / steps / geometry
       → 各底圖分段畫線，保留來源限制
```

`IJourneyProvider` 是伺服器端轉接器介面；NSW、Google 各自實作，開源服務保留既有經過測試的實作。新增供應商只需轉成共用 `JourneyStep`，不讓 UI 依賴 GTFS、Google 或 NSW 原始格式。Apple 原生轉接器也回傳相同模型。底圖與來源可以分開選，但 **Google 路線只能畫在 Google 底圖**；含 Google live 地點資料的行程只允許 Google 路線來源。來源、日期、交通方式改變就取消舊查詢並清空結果，不自動換供應商，也不保存 Google 路線回應。

| 預設來源 | 步行 / 開車 | App 內公車號碼與轉乘 | 啟用條件 |
|---|---|---|---|
| 開源 | Valhalla 實際道路线形 | OTP + GTFS；GTFS-RT 選配 | 自己的服務與有效資料 |
| NSW + Valhalla | Valhalla | TfNSW Trip Planner | Trip Planner API 授權金鑰 |
| Google | Google Routes | Google Transit route steps | Google 底圖、服務端金鑰與 `AllowPaidServices=true` |
| Apple（iOS） | MKDirections 原生路徑 | MapKit 不提供完整公交路徑 / 班次明細 | 公交另選 NSW / 開源；或明確開啟 Apple Maps 的首個公交區間 |

Apple 的 `.transit` 在 MapKit 只支援 ETA 計算，不是完整導航路徑；因此不能承諾三家公交能力完全相同。切換來源使用相同畫面與資料模型，能力差異在選擇時明示。既有 Ferrostar 前台逐段導航仍是開源步行功能，只在 OSM / open 模式提供；其他來源目前提供道路與交通規劃預覽。

## 不部署 OTP 的 NSW 接法

1. 在 [TfNSW Open Data Hub](https://opendata.transport.nsw.gov.au/) 註冊並取得 **Trip Planner APIs** 的存取權。GTFS-RT 訂閱金鑰不一定有 Trip Planner 權限，需確認授權項目。
2. 在本機 `http://127.0.0.1:8787/setup` 填寫 `TFNSW_API_KEY`，或由服務端環境變數提供。沿用私有 `.secrets.json` 的原子寫入與僅使用者可讀權限，不放進手機、網址或 Git。請不要在聊天或截圖中貼金鑰。
3. App 的「交通路線規劃」選 **NSW + Valhalla**，再選大眾運輸並查詢。固定呼叫官方 `https://api.transport.nsw.gov.au/v1/tp/trip`，授權放在 HTTP header，採悉尼當地日期 / 時間。
4. 回應的 `coords` 使用服務給的完整路徑，解析公車號碼、方向、上下車站與預定 / 估計時間。只在明確即時旗標與估計時間同時存在時標為即時；未提供站數或站台則留空，不推測。

`/health` 的 `nswTransitConfigured` 只代表有設定金鑰，不保證授權 / 配額可用。沒有金鑰時會在任何道路請求前拒絕包含公交的整份查詢。此接法不需自行下載 GTFS；保留下面 OTP 方式，供需要全開源引擎、自管資料或其他城市時使用。Mobility Database / Transitland 可找 feed，但不會自動替 App 產生轉乘規劃。


- 路線面板參考「上方起終點摘要、下方可收合行程清單」的操作方式，保留 App 綠色配色。
- 可選全部步行／全部開車／全部大眾運輸，也可逐段選擇。例如去程搭公車、下一站步行。
- 步行用藍色密集小圓點虛線（Android OSM 改用圓點圖層加細白邊，不鋪連續白色底線；點距隨縮放調整，避免與底圖步道混淆），開車用綠色細實線，大眾運輸用藍色細實線，包含公車、Metro、Train、輕軌與渡輪。不同交通段分別繪製，缺失的路線形狀不補成可導航直線。
- 公車顯示資料來源提供的路線號碼／名稱、方向、上下車站、出發抵達時間、候車／銜接時間、站數與站台（資料有提供時）。缺少路線號碼明確標示未提供。
- 公共交通預設是時刻表班次；僅 OTP / NSW 明確回傳即時更新時顯示即時。Google 轉接器不推定 GTFS-RT 狀態。
- 每兩個景點之間各查一次；下一段從前段實際抵達時間加上該景點停留分鐘開始查。候車與轉乘算入交通總時間，景點停留另列，API 另回傳含停留的總經過時間。
- 日期／時間或交通模式改變會取消並清除舊結果，需重新查詢。所有畫面時間以 Australia/Sydney 顯示（含夏令時間）。
- 目前為交通規劃與分段指引；只有開源來源、OSM 底圖且全部步行時可進入既有 Ferrostar 步行導航。開車沒有行車語音導航、即時車流或停車估時。混合開車與其他交通時，車輛安排由使用者自行處理。
- 需預約的 on-demand 服務與未支援模式明確拒絕，不包裝成一般公車。

## 取得開放資料

1. 從 [TfNSW 開放資料目錄](https://opendata.transport.nsw.gov.au/) 取得悉尼／NSW 官方 GTFS 靜態時刻表，依需求包含公車、火車、輕軌與渡輪等 feed。下載或 API 存取可能需要免費開發者帳號／金鑰；不需將金鑰放入 App 或提交 Git。
2. 取得涵蓋相同地區的 OSM `.osm.pbf`，可參考 [Geofabrik 下載目錄](https://download.geofabrik.de/)。只建需要的區域可降低記憶體與建圖時間。
3. 確認 GTFS 的 `calendar.txt`／`calendar_dates.txt` 覆蓋查詢日期，且 `routes.txt`／`trips.txt` 與路線形狀齊全。過期 feed 會查不到班次；不要把舊資料當成即時服務。
4. 保留資料來源要求的授權與署名；OSM 底圖持續顯示 OpenStreetMap contributors。

本專案不重新發布 NSW 資料包；`infra/transit/data/` 已排除 Git，適合放本機 feed、設定與 OTP graph。此串接使用開源引擎及開放資料，不代表主機、圖磚或託管 API 沒有使用限制或營運成本。

## 本機部署 OTP

先安裝 Docker，下載前述資料後，在專案根目錄執行：

```sh
python3 scripts/prepare_transit_data.py \
  --gtfs /absolute/path/sydney-buses.zip \
  --gtfs /absolute/path/sydney-trains.zip \
  --osm /absolute/path/sydney.osm.pbf

docker compose -f infra/transit/compose.yaml run --rm otp --build --save
docker compose -f infra/transit/compose.yaml up -d
```

匯入工具檢查必要 GTFS 檔案與路線欄位後複製資料，檔名含 `gtfs` 以供 OTP 偵測；不覆寫既有檔案／graph。OTP 建圖仍會做完整資料驗證。首次建圖需要時間與足夠記憶體，Compose 的 JVM 上限預設為 4 GB，可依區域調整。映像固定為 `opentripplanner/opentripplanner:2.10.0`，僅公開本機 `127.0.0.1:8801`。

以以下設定重新啟動既有 ASP.NET Core 後端；原本 Valhalla 位址保持自己的設定：

```sh
Transit__OtpGraphqlUrl=http://127.0.0.1:8801/otp/gtfs/v1 \
Navigation__ValhallaUrl=http://127.0.0.1:8002/route \
dotnet run --project server-dotnet/SydneyBuddy.Api
```

`/health` 的 `transitConfigured=true` 只表示端點設定正確，不保證 OTP 已啟動或 feed 日期可用。正式部署請使用自己的 OTP／Valhalla 服務；不跟隨 HTTP 重新導向，非本機位址要求 HTTPS。App 不接受客戶端任意指定上游網址。GTFS-RT 需另外依 [OTP 即時更新設定](https://docs.opentripplanner.org/en/v2.10.0/Updaters/) 配置，本次未啟用。

## API 與檔案

`POST /journey/plan` 範例（不是班次資料）：

```json
{
  "contentSource": "open",
  "routingProvider": "nsw",
  "mapProvider": "osm",
  "departureTime": "2026-10-23T10:00:00+11:00",
  "points": [
    { "name": "起點", "lat": -33.8611, "lng": 151.2126 },
    { "name": "景點", "lat": -33.8568, "lng": 151.2153, "stayMinutes": 60 },
    { "name": "返回點", "lat": -33.8611, "lng": 151.2126 }
  ],
  "modes": ["transit", "walk"]
}
```

`routingProvider` 接受 `open`（預設）、`nsw`、`google`；`mapProvider=google` 才允許 Google 路線。`apple` 由 iOS 裝置端處理，後端不假裝支援。

限制 2–12 個站點，模式陣列長度必須是站點數減一。`departureTime` 必須包含時區。回應包含 `legs[].steps[]` 的交通方式、班次與路線座標；失敗回傳 HTTP 422 及適合畫面顯示的訊息，不回傳半份規劃或上游內部錯誤。

| 檔案 | 用途 |
|---|---|
| `server-dotnet/SydneyBuddy.Api/Services/JourneyService.cs` | 驗證輸入、逐段排程、呼叫 OTP、交通中立資料與 polyline5/6 解析 |
| `server-dotnet/SydneyBuddy.Api/Services/JourneyProviders.cs` | `IJourneyProvider`、Google / NSW 查詢及交通中立正規化，固定端點與無秘密錯誤訊息 |
| `server-dotnet/SydneyBuddy.Api/Services/NavigationService.cs` | 既有導航保持步行；道路預覽另提供 walk／drive 受限模式 |
| `ios/SydneyBuddy/JourneyPlannerView.swift` | iOS 交通面板、模式與時間選擇、班次資訊及請求取消 |
| `android/.../WalkingRouteDots.kt` | 沿道路幾何按距離取樣；地圖縮放時更新圓點間距，維持小圓點白邊與可見性 |
| `android/.../JourneyPlanner.kt` | Android 對應 UI、交通資料模型與 API 請求 |
| `MapView.swift`／`MapProvider.kt`／`RouteMap.kt` | 各底圖共用座標並分段繪製交通線型 |
| `infra/transit/compose.yaml` | 固定版本的本機 OTP 建圖／啟動設定 |
| `scripts/prepare_transit_data.py` | 檢查並準備本機開放資料，不做網路請求或保存金鑰 |
| `tests/fixtures/otp-journey-test.json` | 合成 OTP GraphQL 回應，用於後端契約測試 |
| `tests/fixtures/journey-plan-test.json` | 合成交通中立回應，用於雙平台解析與畫面測試 |

## 驗證與尚未驗證的部分

- 後端 104 個測試通過，包含 OTP 請求／正規化、公車資訊、逐段停留時間、drive／walk 隔離、無班次、預約服務、錯誤與取消，以及既有資料庫 API 回歸。
- iOS 12 個 XCTest 通過，包含交通請求、班次解析、未設定服務時的錯誤與測試畫面；Google SDK 有編譯但未配置有效金鑰實測。
- Android 24 個單元測試、4 個交通操作測試、Lint 與編譯通過；另有 7 個既有非 LargeTest 回歸測試通過；首頁新增入口造成的按鈕位置問題已修正，規劃流程與底圖切換重新通過。
- Google / NSW 轉接器以合成原始回應驗證：來源與底圖限制、付費開關、座標順序、公車資訊、時間、缺少線形、預約服務與錯誤。尚無金鑰，不聲稱已對兩家完成真實網路驗證。
- Apple 實際步行路線於模擬器手動查詢成功：環形碼頭 → 植物園 → 歌劇院 → 岩石區 → 當代藝術館 → 返回，約 3,139 公尺 / 42 分鐘；路徑由 MapKit 回傳，沒有用站點直線補畫。
- 測試資料 `TEST-01` 不是 TfNSW 班次。尚未設定 NSW API key，沒有實際下載／匯入 NSW GTFS、啟動 OTP graph 或驗證真實轉乘；需部署上面的服務後才能查實際班次。
- 本次對新版 API 實際查詢 Valhalla 步行成功：環形碼頭至歌劇院約 707 公尺、58 個路徑頂點。NSW 未設定與 Google 付費保護均實際回傳明確 422。
- 前版新 API 對開發用 Valhalla 做一次實際開車預覽成功（環形碼頭至歌劇院約 804 公尺／229 秒）；無 OTP 時回傳明確的 422 提示。
- 不含票價、即時車流、開車語音導航、戶外 GPS 或付費 Google 服務驗證。

依據：[NSW Trip Planner APIs](https://opendata.transport.nsw.gov.au/data/dataset/trip-planner-apis)、[Google Routes 公交](https://developers.google.com/maps/documentation/routes/transit-route)、[Google 地圖顯示政策](https://developers.google.com/maps/documentation/routes/policies)、[Apple transit 支援範圍](https://developer.apple.com/documentation/mapkit/mkdirectionstransporttype/transit)、[OTP GTFS GraphQL](https://docs.opentripplanner.org/api/dev-2.x/graphql-gtfs/queries/planConnection)、[OTP 資料來源](https://docs.opentripplanner.org/en/v2.10.0/Data-Sources/)、[官方容器建圖方式](https://docs.opentripplanner.org/en/v2.10.0/Container-Image/)。

2026-10-23 路線可見性修正：以錄製的五段步行行程重現先載入地圖再規劃，確認小圓點與 OSM 原有步道混淆；Android OSM 改用獨立圓點圖層、藍色細點及小白邊，依鏡頭縮放重新取樣。新增畫面像素斷言，驗證規劃、放大／縮小及重新查詢後均有路徑，路線保持在站點標記下方。

iOS 全天地圖的補正：Apple 底圖同樣提供步行路線入口；展開全天地圖時，Apple / OSM 示例行程會在尚無路徑時查詢對應道路服務。交通面板的成功規劃結果會同步到首頁和全天地圖，保留各段交通方式與線型。Google 路線仍限 Google 底圖，切換底圖清除舊結果。新增的 2 個 iOS 回歸測試已通過；新版完成 Xcode 編譯及全部 14 個 XCTest，並已重新安裝到 iPhone 17 Pro 模擬器。手動以 OSM / Valhalla 實際規劃五段步行行程成功，約 3,692 公尺／52 分鐘；交通面板、首頁與全天地圖均確認顯示藍色密集圓點，規劃結果可同步。此輪未驗證 Google 真實底圖。

步行樣式統一：OSM / Apple / Google 均改藍色小圓點並縮短間距（iOS 原生點距約縮短 38%、Android OSM 螢幕間距約縮短 31%、Google 點距同步縮短）。保留細點與白邊以區別底圖，大眾運輸維持藍色細實線。

藍色密集點線調整後，iOS App Swift 型別檢查通過。Android 已重新完成 Debug 編譯、24 個單元測試、11 個模擬器操作測試及 Lint，全部通過；包含錄製道路幾何在規劃、放大／縮小和重新查詢後的藍色圓點畫面斷言，並已安裝及開啟新版 App。此輪道路畫面測試使用錄製的 Valhalla 路徑，沒有重複呼叫外部路由服務；Google 尚無有效金鑰，未完成 Google 底圖的真實畫面驗證。


2026-10-23 步行路線螢幕尺寸適配：以 390 pt／dp 為參考，藍點直徑 4.4、白邊 0.65、中心間距 10，依地圖短邊限幅縮放至 90%–115%。iOS OSM / Apple 使用透明的螢幕座標繪圖層，沿道路幾何重新取樣，縮放與拖移時更新；合併鄰近的來回路段圓點，並避開站點標記。Android OSM 依實際地圖容器尺寸設定圓點，在鏡頭停止後按比例尺更新地理間距。Google iOS 的間距也改為依鏡頭比例尺換算，Android Google 使用 dp 轉像素，尚無有效 Google 金鑰實測。Android 此版 25 個單元、11 個操作測試及編譯／Lint 通過。

此版 iOS 已完成編譯及 14 個 XCTest，重新安裝後實測 OSM 全天道路規劃，並確認雙擊放大後仍維持相同的螢幕圓點大小與間距；兩台模擬器均已更新。iOS Apple 共用同一繪圖層，此輪手動畫面驗證使用 OSM。
