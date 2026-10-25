# 官方景點票價查核紀錄（2026-10-25）

本次涵蓋 `server/places.json` 內建的 8 個雪梨景點，共 16 筆參考記錄。不是全球景點的即時票價服務，也沒有把搜尋結果的所有商家標為已核實。

資料存於 `shared/activity-offers.json`，經 `scripts/sync_activity_offers_zetaris.py` 存入獨立 PostgreSQL 公開資料表，再由真實 Zetaris `travel_activity_offers` 提供給 iOS／Android。保留場館識別配對，餐廳不會再因距離接近而拿到其他場館的票價。

| 景點 | 本次核對的票價／狀態 | 官方來源與限制 |
| --- | --- | --- |
| Sydney Opera House | 標準導覽：成人預訂 AUD 50、當日 AUD 55；5–15 歲預訂 AUD 30；優惠預訂 AUD 40；家庭預訂 AUD 130；Tour & Dine 成人預訂 AUD 84 | [標準導覽](https://www.sydneyoperahouse.com/tours/sydney-opera-house-tour)、[導覽用餐](https://www.sydneyoperahouse.com/tours/tour-and-dine)。家庭為整組 2 成人＋2 兒童，非每人費用；2027-04-01 起標準導覽另列新價格；不含表演門票。 |
| Royal Botanic Garden Sydney | 一般入園免費 | [訪客頁](https://www.botanicgardens.org.au/royal-botanic-garden-sydney/plan-your-visit)。特殊售票活動另計，不能當成所有園內活動皆免費。 |
| Australian Museum | 一般入場與常設展免費 | [入場資訊](https://australian.museum/visit/admission/)。特展、工作坊及部分活動另計。 |
| Art Gallery of NSW | 一般入場免費 | [官方訪客頁](https://www.artgallery.nsw.gov.au/visit/)可讀版本較舊，另以 [Service NSW 近期官方名錄](https://www.service.nsw.gov.au/nswgovdirectory/art-gallery-of-nsw)交叉核對。多數展覽免費，特展須另查。沒有把舊開放時間匯入。 |
| Queen Victoria Building | 歷史導覽每人 AUD 25；0–12 歲免費但須付費成人陪同 | [官方導覽頁](https://www.qvb.com.au/qvb-history-tour)。此為選購、需預訂的約 60 分鐘導覽，不是建築一般入場費，金額含 GST。 |
| Museum of Contemporary Art Australia | 2026-10-25 日期的一般入場：成人 AUD 25、優惠／大專學生 AUD 20；未滿 18 歲與會員免費 | [官方訪客資訊](https://www.mca.com.au/visitor-information/)、[官方一般入場售票頁](https://tix.mca.com.au/events/a5f499c1-d58d-1808-4770-4934715a3779)。經一般瀏覽器選日期後讀取，不加入購物車、不登入。不含當期主要特展，結帳另收服務／交易費；總額未知，其他日期須重新查價。0–12 歲需由付費持票者陪同。 |
| The Rocks | 街區統一票價未核實：`amount=null` | [官方免費活動頁](https://www.therocks.com/whats-on/campaigns/free-activities)。官方列免費散步、文化活動，但不能把所有導覽、餐飲與個別場館都視為免費。 |
| Darling Harbour | 街區統一票價未核實：`amount=null` | [官方免費活動頁](https://www.darlingharbour.com/whats-on/campaigns/free-things-to-do)。免費公共藝術或活動不代表水族館、中國花園等設施免費。 |

## 免費、已知與未知

- `pricingStatus=free`：有明確免費條件，`amount=0`。
- `pricingStatus=known`：已核對正數價格，保留票種及限制。
- `pricingStatus=unknown`：官網未提供可確認的統一價格，`amount=null`。App 顯示「票價待確認」，不顯示零元或 NaN。
- `reviewBy` 是內部重查日期，不是有效期限。MCA 因查詢指定日期，重查期限設為翌日；卡片條件保留實際查價日期。
- 全部仍固定 `budgetEligible=false`。沒有直接把家庭票當成單人費用，也沒有用參考票價覆蓋已儲存行程或餐廳預算。

## 後續維護

這版是依官網逐一核對的參考集合，沒有背景排程，也不會在每次打開 App 時自動爬官網。新增其他景點時，先確認官方場館網址、場館 ID／別名及座標，再整理票種、數值、條件與查核日期；無法確認則保留未知。遇到驗證頁或無可用售票日期，不把第三方文章的舊價格補成官網現價。

更新共享資料後執行：

```sh
python3 scripts/sync_activity_offers_zetaris.py --platform-env /path/to/zetaris-platform/.env
```

此操作只更新活動參考集合及對應 Zetaris view，不修改天氣或使用者資料。操作教學及 API 規格見 `ACTIVITY-OFFERS.md`。
