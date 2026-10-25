# 活動與參考票價：App / PostgreSQL / Zetaris

## 已接通的流程

`經查證的官方活動資料 → 公開來源 PostgreSQL → 真實 Zetaris travel_activity_offers → ASP.NET Core /activities/search → iOS / Android 景點介紹`

本機已匯入兩項雪梨歌劇院活動：成人預訂導覽 AUD 50、成人預訂導覽加用餐 AUD 84。資料查核日 2026-10-25；來源分別是 [官方導覽](https://www.sydneyoperahouse.com/tours/sydney-opera-house-tour) 與 [官方導覽加用餐](https://www.sydneyoperahouse.com/tours/tour-and-dine)。只保存整理後的事實、價格條件與來源連結，不複製官網介紹、照片或評論。這些是官方公開資訊，並不宣稱是開源資料集。

`shared/activity-offers.json` 是版本控制內的公開參考集合。App 的景點介紹只顯示已明確配對此場館的活動。使用已審核 venueIds，或完整場館名稱／venueAliases（正規化後完全相等），並要求座標相距不超過 250 公尺；不使用附近距離或名稱子字串推測關聯。餐廳若沒有自己的資料，顯示尚無已確認的活動票價，不套用歌劇院內容。

## 切換資料路徑

沿用 App 設定的 `X-Travel-Data-Source: direct|zetaris`，與底圖選擇獨立：

- direct：後端讀取共享公開參考檔。
- zetaris：後端以固定 SELECT 讀取 `sydneybuddy_travel.travel_activity_offers`，不使用直接來源兜底，也不沿用一般 POI 快照快取。

未設定表格或連線失敗會顯示活動無法載入，可重試；不冒充附近沒有活動。

## 匯入到本機 Zetaris

先依 `infra/zetaris/migration/SERVICE-STARTUP.md` 啟動來源 PostgreSQL 與 Zetaris 官方本機服務，並完成 `sydneybuddy_travel` datasource 的基本註冊。

```sh
python3 scripts/sync_activity_offers_zetaris.py \
  --platform-env /path/to/zetaris-platform/.env
```

可加 `--input /path/to/reviewed-activity-offers.json`。預設讀取 `shared/activity-offers.json`，先驗證必填欄位、日期、明確幣別與唯一 ID，只輸出允許的公開欄位；不輸出多餘帳戶欄位。沒有遠端抓網頁行為。

腳本以交易更新獨立的 `activity_offer_records(id,payload)`，建立唯讀 `travel_activity_offers` view，授權既有 `travel_reader`，註冊到真實 Zetaris 並比對完整 payload。重跑會以輸入集合取代這一張活動表的內容，不更動景點、天氣、餐廳或使用者資料。登入資訊只從指定的本機環境檔讀取，不寫入資料檔、版本控制或 App。

**此腳本目前僅支援既有本機 Docker 與 loopback Zetaris**。現有 AWS 快速打包流程尚未包含這個新增集合；請勿認為推送程式碼就會自動把資料部署到 AWS。後續遷移需新增活動表的匯出、還原與註冊步驟。

## API

```http
POST /activities/search
Content-Type: application/json
X-Travel-Data-Source: zetaris

{"lat":-33.8568,"lng":151.2153,"radius":1000,"query":"","placeId":"opera","placeName":"悉尼歌剧院"}
```

必須提供 placeId 與 placeName 才能得到活動；舊版請求未帶識別資料會得到空集合，不能退回附近推薦。範圍 100–5000 公尺、最多 30 筆，仍套用 250 公尺的場館核對上限；可依活動或場館名稱過濾。回傳 `dataSource` 與 `offers`。欄位包括 `id,title,venueName,lat,lng,amount,currency,ticketType,conditions,sourceName,sourceUrl,observedOn,reviewBy,priceType,budgetEligible,stale,distanceMeters`。

`reviewBy` 是內部重新查核期限，**不是活動保證有效期限**。超過查核期限或距上次查核超過 30 天，顯示票價待確認。官方導覽頁另列 2027-04-01 起調價，因此卡片條件提醒須按出遊日期重新確認。

所有活動都固定 `budgetEligible=false`，不寫入行程 `cost`。參考票價不表示已選日期／票種、不保證有名額；導覽用餐套餐也不是餐廳單點菜單價。要計入預算，之後須另設明確的使用者確認與估算版本流程。

## Klook 與其他來源

Klook 前次一般 HTTP 測試回傳 403 驗證頁，因此目前沒有任何 Klook 實測商品被匯入。原型見 `KLOOK-PROTOTYPE.md`。若有取得授權的商品匯出，可整理成同一格式再匯入；沒有座標的商品先確認場館，不能只憑名稱自動捏造位置。

## 驗證

- Python：`python3 -m unittest discover -s scripts/tests -p 'test_*offers.py'`。
- .NET：`ActivityOfferTests` 驗證範圍／名稱過濾、過期提示、幣別與來源驗證、禁止扣預算、資料路徑與無靜默回退。
- 本機實際同步結果：`verified, activityOffers=2, realZetaris=true`。
- 本機 API direct 與 zetaris 路徑皆返回 HTTP 200、兩項相同的活動與價格，均不計預算。
- iOS 112 項測試通過；模擬器實際開啟景點介紹，確認兩張活動卡片、AUD 50／84、距離與來源日期均可見。
- Android 編譯與單元測試通過，APK 已安裝至本機模擬器。

2026-10-25 配對修正：明確 venueIds／venueAliases 會一起同步到 PostgreSQL 與 Zetaris。回歸測試涵蓋附近餐廳不配對、缺少場館識別不配對、名稱包含不配對、同名遠處分店不配對，以及已審核別名可配對。

## 官網票價擴充（2026-10-25）

最新集合已擴充為 8 個內建場館、16 筆參考記錄，逐項來源與限制見 [官網票價查核紀錄](OFFICIAL-ADMISSION-PRICES.md)。價格新增 `pricingStatus=known|free|unknown`；未知 `amount=null`，明確免費才是 `amount=0`，兩個手機平台均分開顯示。原本兩項活動的實測是初版紀錄，最新數量以共享集合及查核紀錄為準。

## 景點清單票價摘要

`POST /activities/summaries` 接受 `places:[{id,name,lat,lng}]`，上限 80 個景點，一次讀取所選直接／Zetaris 資料來源，再使用相同的嚴格場館配對規則。

摘要只使用未過期、明確已知、適用成人／一般訪客的票價。優先一般入場，其次獨立導覽，最後成人套裝；同一類中取最低價，不平均不同產品、兒童票、優惠票、家庭整組票或不同幣別。缺少可比較價格則不產生摘要，清單保留費用未知。

iOS／Android 搜尋清單會自動批次取得摘要，顯示例如「成人導覽 A$50 起」「成人票 A$25 起」或「一般入場免費」。摘要不更新行程成本；詳細票種、條件及來源仍由 ⓘ 查看。資料來源切換或換一批結果時重查，載入失敗不顯示先前來源的摘要。
