# 開放交通資料與多模式路線

本地開發分支：`feat/itinerary-map-overview`。

**目前完成的是服務串接、雙平台介面與本機測試。尚未匯入 NSW 真實 GTFS／啟動 OTP，所以真實公車班次尚未啟用。** App 不會在查不到資料時產生假的公車號碼。測試圖中的 `TEST-01` 只存在於測試 fixture，畫面明確標示非真實班次；正式程式不提供 fixture 選單或自動退回測試資料。

## 架構

```text
SwiftUI / Jetpack Compose
  └─ POST /journey/plan（每段模式、出發時間、站點與停留分鐘）
       ├─ walk / drive → Valhalla（OSM 道路網）
       └─ transit → OpenTripPlanner 2.10 GTFS GraphQL API
                         ├─ OSM 道路網
                         ├─ GTFS 時刻表、站點、路線號碼與路線形狀
                         └─ GTFS-RT（選配；未配置時不是即時班次）
```

地圖底圖與交通規劃分離；現有 OSM／Apple／Google 底圖選擇保留，畫面只收到與 SDK 無關的座標。沒有加入 Google Routes、Places 或 NVIDIA 呼叫。含 Google live 地點資料的行程維持原本的來源限制，不送進開源規劃服務。

- 路線面板參考「上方起終點摘要、下方可收合行程清單」的操作方式，保留 App 綠色配色。
- 可選全部步行／全部開車／全部大眾運輸，也可逐段選擇。例如去程搭公車、下一站步行。
- 步行用藍色點線，開車用綠色實線，大眾運輸用橙色實線。不同交通段分別繪製，缺失的路線形狀不補成可導航直線。
- 公車顯示資料來源提供的路線號碼／名稱、方向、上下車站、出發抵達時間、候車／銜接時間、站數與站台（資料有提供時）。缺少路線號碼明確標示未提供。
- 公共交通預設是時刻表班次，只有 OTP 標示該段有即時資料時才顯示即時。
- 每兩個景點之間各查一次；下一段從前段實際抵達時間加上該景點停留分鐘開始查。候車與轉乘算入交通總時間，景點停留另列，API 另回傳含停留的總經過時間。
- 日期／時間或交通模式改變會取消並清除舊結果，需重新查詢。所有畫面時間以 Australia/Sydney 顯示（含夏令時間）。
- 目前為交通規劃與分段指引；只有全部步行時可進入既有 Ferrostar 步行導航。開車沒有行車語音導航、即時車流或停車估時。混合開車與其他交通時，車輛安排由使用者自行處理。
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
  "departureTime": "2026-10-23T10:00:00+11:00",
  "points": [
    { "name": "起點", "lat": -33.8611, "lng": 151.2126 },
    { "name": "景點", "lat": -33.8568, "lng": 151.2153, "stayMinutes": 60 },
    { "name": "返回點", "lat": -33.8611, "lng": 151.2126 }
  ],
  "modes": ["transit", "walk"]
}
```

限制 2–12 個站點，模式陣列長度必須是站點數減一。`departureTime` 必須包含時區。回應包含 `legs[].steps[]` 的交通方式、班次與路線座標；失敗回傳 HTTP 422 及適合畫面顯示的訊息，不回傳半份規劃或上游內部錯誤。

| 檔案 | 用途 |
|---|---|
| `server-dotnet/SydneyBuddy.Api/Services/JourneyService.cs` | 驗證輸入、逐段排程、呼叫 OTP、交通中立資料與 polyline5/6 解析 |
| `server-dotnet/SydneyBuddy.Api/Services/NavigationService.cs` | 既有導航保持步行；道路預覽另提供 walk／drive 受限模式 |
| `ios/SydneyBuddy/JourneyPlannerView.swift` | iOS 交通面板、模式與時間選擇、班次資訊及請求取消 |
| `android/.../JourneyPlanner.kt` | Android 對應 UI、交通資料模型與 API 請求 |
| `MapView.swift`／`MapProvider.kt`／`RouteMap.kt` | 各底圖共用座標並分段繪製交通線型 |
| `infra/transit/compose.yaml` | 固定版本的本機 OTP 建圖／啟動設定 |
| `scripts/prepare_transit_data.py` | 檢查並準備本機開放資料，不做網路請求或保存金鑰 |
| `tests/fixtures/otp-journey-test.json` | 合成 OTP GraphQL 回應，用於後端契約測試 |
| `tests/fixtures/journey-plan-test.json` | 合成交通中立回應，用於雙平台解析與畫面測試 |

## 驗證與尚未驗證的部分

- 後端 90 個測試通過，包含 OTP 請求／正規化、公車資訊、逐段停留時間、drive／walk 隔離、無班次、預約服務、錯誤與取消，以及既有資料庫 API 回歸。
- iOS 10 個 XCTest 通過，包含交通請求、班次解析、未設定服務時的錯誤與測試畫面；Google SDK 有編譯但未配置有效金鑰實測。
- Android 19 個單元測試、3 個新交通操作測試、Lint 與編譯通過；另有 7 個既有非 LargeTest 回歸測試通過；首頁新增入口造成的按鈕位置問題已修正，規劃流程與底圖切換重新通過。
- 測試資料 `TEST-01` 不是 TfNSW 班次。沒有實際下載／匯入 NSW GTFS、啟動 OTP graph 或驗證真實轉乘；需部署上面的服務後才能查實際班次。
- 新 API 對開發用 Valhalla 做一次實際開車預覽成功（環形碼頭至歌劇院約 804 公尺／229 秒）；無 OTP 時回傳明確的 422 提示。
- 不含票價、即時車流、開車語音導航、戶外 GPS 或付費 Google 服務驗證。

依據：[OTP GTFS GraphQL](https://docs.opentripplanner.org/api/dev-2.x/graphql-gtfs/queries/planConnection)、[OTP 資料來源](https://docs.opentripplanner.org/en/v2.10.0/Data-Sources/)、[官方容器建圖方式](https://docs.opentripplanner.org/en/v2.10.0/Container-Image/)。
