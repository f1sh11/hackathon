# 餐廳菜單、花費與評論資料整合評估

評估日期：2026-10-25（Australia/Sydney）。這份是來源評估，未新增外部資料抓取或訂閱。

建議優先級：餐廳官方菜單＋使用者匯入內容 → 小量可追溯的測試資料 → 通過實測的外部 API／Actor。現有 ASP.NET Core／PostgreSQL／Zetaris 架構可以沿用。

| 選項 | 適合用途 | 目前判斷 |
| --- | --- | --- |
| 餐廳官網菜單／店家提供資料 | 菜名、單品價格、套餐與日期 | 第一優先。讀網頁結構化資訊，無結構才解析 HTML／PDF；不能假設每家都有 JSON-LD。 |
| 已有社群貼文匯入 | 推薦菜色、作者實際消費、評論原文 | 第一優先。沿用已完成的證據萃取與地圖位置比對，不將單次花費視為店家均消。 |
| Kaggle CSV／JSON | 冷啟動、搜尋和預算模型測試 | 有條件。先取得具體 dataset URL、原來源、授權、資料日期與欄位定義。這次沒有確認使用者舉例的 Sydney Restaurant Listings & Reviews Dataset 的實際頁面，不能宣稱已找到可用且最新的雪梨資料集。 |
| Open Price Engine | 外部餐飲／連鎖菜單價格的候選補充 | 先驗證，不作核心依賴。官方文件有餐廳端點和澳洲覆蓋數字，但查到的 Swagger 端點清單沒有餐廳項目；尚未取得 API key 或實測雪梨商家。 |
| Apify Actor | 快速驗證特定來源的菜單／評論抓取 | 可選。須評估每個 Actor 的原始碼授權、成本、輸出與失敗率，不能把 SDK 開源等同於 Actor 免費、資料可任意再發布。 |
| MediaCrawler | 中文社群研究／獨立實驗 | 不建議作商用產品的預設底座。官方 README 限制商業用途；平台表未列大眾點評。 |
| Crawlee／Scrapy | 自建官網資料蒐集工作 | 有需要再加；不是現成餐廳資料庫。少量來源可先由 .NET 實作，避免為了幾家店增加額外服務。 |

## Open Price Engine 的實際限制

[官方文件](https://www.openpriceengine.com/documentation/) 描述餐廳名稱、菜單項目及價格歷史 API，並在 Restaurant Database 列 Australia 100。這是官方宣稱的涵蓋範圍，不代表 100 家雪梨分店已逐家驗證，也不證明能按座標查詢獨立餐廳。文件說免費金鑰有效一個月；[MCP 文件](https://www.openpriceengine.com/documentation/mcp/) 則要求 Premium，且介紹的是雜貨與電子產品工具。

[公開 Swagger](https://www.openpricengine.com/docs) 在本次查核顯示雜貨、電子、能源、保險及房地產端點，未列出說明頁中的餐廳端點。因此接入前應以金鑰驗證實際 endpoint、澳洲／雪梨店名、更新時間、內用／外送價格語意及重用條款。未確認原始碼和資料授權前，只能稱為第三方 API 候選，不能直接稱整套系統及資料皆為自由開源。

## 爬蟲工具與資料分開評估

[MediaCrawler 官方 README](https://github.com/NanmiCoder/MediaCrawler) 的平台表列小紅書、抖音、快手、B 站、微博、貼吧、知乎等，沒有列大眾點評；不能根據第三方介紹承諾開箱即用。README 的非商業限制也使它不適合作為未來商業 App 的預設依賴。

[Apify Actor 計價文件](https://docs.apify.com/actors/running/actors-in-store) 說明按事件或平台資源收費。個別 Google Maps／Yelp／Uber Eats Actor 能執行，不等於其資料可長期保存、跨底圖展示或再發布；要逐一確認輸出及資料使用條件。外送頁面的價格亦應保留為「外送渠道」資料，不直接代換內用價格。

可自行控制的替代方案：[Crawlee](https://github.com/apify/crawlee) 或 [Scrapy](https://docs.scrapy.org/en/latest/) 抓取可使用的餐廳官網資料，再交現有 .NET API 處理。結構化欄位可參考 [Schema.org MenuItem](https://schema.org/MenuItem)／Offer；標準支援這些欄位不代表目標網站一定提供它們。

## 建議接入現有架構的方式

1. 資料取得：官網菜單、店家提供檔案、授權資料集、使用者貼文、選定的 API／Actor，各自是可替換來源。
2. ASP.NET Core：解析資料、保存證據、統一幣別與價格單位；比對店家 ID、地址與座標，不能只用同名合併分店。
3. PostgreSQL：分開存 Venue、MenuItem、PriceObservation、ReviewEvidence 與來源中繼資料。
4. Zetaris：讀取整理後的 views，整合既有 OSM／Overture 店家。天氣維持既有不暫存 API 流程。
5. App：顯示菜單、推薦菜色、價格渠道、來源和時間；規劃器只採明確定義的估算。

目前社群匯入保存的是 prices 原文證據與推薦內容，**尚未實作 MenuItem／PriceObservation 的結構化金額資料表、官網菜單爬取或用這些資料自動扣行程預算**。

## 價格與評論如何顯示

- 官網單品價格：`menu_item_price`，帶幣別、份量、分店、渠道及取得時間。
- 作者單次花費：`reported_spend`，保留人數、是否含飲料／小費及原文；不升格為店家均消。
- 資料集價位等級：`price_level`，保留原始定義與版本，不能把 `$$` 自動轉為固定 AUD 金額。
- App 每人預算：`estimated_per_person`，另記計算依據；若未選菜單品項或缺少足夠資訊，顯示「待確認」，不以假定數字冒充已知價格。
- 評論：逐筆保存來源平台、原文證據及日期；不同平台星等／樣本數不直接混成一個無來源的評分。「推薦菜色」只表示來源中的推薦，不能宣稱客觀最好吃。
