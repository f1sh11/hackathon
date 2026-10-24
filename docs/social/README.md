# 社群旅遊靈感匯入

採用 ASP.NET Core 萃取／驗證 → App PostgreSQL 留存草稿 → 人工選定地圖地點並發布 → 專用公開來源 DB → Zetaris 唯讀整合。Zetaris 負責聯邦查詢，不冒充 IG／TikTok／Threads／小紅書爬蟲。現有 OSM／Zetaris 搜尋與工具箱直接顯示已發布的 description、照片與來源連結；Apple／Google 的原生搜尋不會自動混入這批集合，選私房分類或 Zetaris 資料來源查看。

## 這版可以與不可以做的事

- 支援四個平台的來源網址、已取得的貼文文字、最多 20 則評論、Hashtag；使用既有 NVIDIA 設定萃取名稱、推薦菜色、作者花費原文、評論及到達說明。
- 每一筆文字證據必須是原文子字串；價格保留作者原始敘述，**不視為平均客單價或已驗證費用**。沒提供的評分、人氣、營業時間、座標不會生成。
- 使用者挑選 `/poi/search` 的既有地點比對，才可發布；支持一篇貼文的多個景點逐一發布。私房標記只代表作者聲稱，不證明冷門或好吃。
- 可保存有作者、來源及明確重用授權的照片 URL，不下載照片；沒有照片授權時沿用既有地圖照片。`permission-granted` 必須已有作者授權，不能拿來繞過權限。
- **尚未實作四個平台的自動 Hashtag 掃描／登入爬蟲／影片 OCR／語音轉錄。** 尚無平台權限，無法宣稱已抓到真實貼文。可接官方 API、合法資料供應商，或匯入你有權使用的文字／JSON。
- 不需另起 FastAPI 或 LangChain；既有 .NET 服務可直接處理。AI 萃取是選用步驟，沒有 NVIDIA 金鑰時可直接提供已整理的 `mentions`。

## API 操作

先啟動原本 PostgreSQL 與 ASP.NET Core，啟動時會套用 `SocialImports` migration。

1. 準備 JSON：`platform` 為 `instagram|tiktok|threads|xiaohongshu`，`postUrl` 為原貼文 HTTPS 網址，`text` 為實際貼文文字（上限 20,000 字），可選 `comments:[{text}]`。
2. `POST /social/extract`，以上述 JSON 為 body。會呼叫既有 NVIDIA API；只回傳待確認 `mentions`，不寫資料庫。若沒有明確景點，返回 `status:no-mentions`。
3. 將結果 `mentions` 加回原 JSON，`POST /social/import`。回傳 `id` 與 `status:draft`。相同平台網址重送保持原紀錄，不覆寫。URL 的 tracking query 不保存。
4. `GET /social/imports` 查看前 1,000 筆草稿／發布紀錄。從 `/poi/search` 找到正確商家後，呼叫：

```json
{
  "mentionIndex": 0,
  "placeId": "請填搜尋回傳的實際 ID",
  "placeName": "請填搜尋回傳的實際名稱",
  "lat": -33.865,
  "lng": 151.212
}
```

傳至 `POST /social/{id}/publish`。座標是搜尋中心；最終位置只採已知地圖資料。範圍為中心附近 500 米，選錯位置或餐飲類別會拒絕。再次發布相同 mention 更新該項，多項會並存。並行更新衝突會回 409。

5. `GET /social/published` 只提供發布後的 POI 集合；預設直接來源的 OSM／私房搜尋立即可見。工具箱仍把未驗證費用視為未知，不把作者單次消費當成預算定價。

萃取格式：

```json
{
  "mentions": [{
    "name": "必須在 quote 中逐字出現的店名",
    "kind": "restaurant",
    "quote": "貼文中的完整引句",
    "dishes": [{"name":"原文菜名", "quote":"含該菜名的原文引句"}],
    "prices": [{"quote":"包含金額和幣別的原文引句"}],
    "reviews": [{"quote":"原文評論"}],
    "accessQuote": "可選的原文交通介紹",
    "hiddenGemQuote": "可選的原文私房推薦敘述"
  }]
}
```

`kind` 也可為 `sight`。每篇最多 10 個地點，每種證據最多 10 筆。`photos` 是可選的陣列，元素為 `{url,pageUrl,title,artist,license}`；license 支援 `CC0`、`CC BY 4.0`、`CC BY-SA 4.0` 或 `permission-granted`。請遵守照片原授權署名要求。

## 同步到真正的 local Zetaris

使用既有 `infra/zetaris/compose.source.yaml` 的獨立公開來源資料庫，**不把 App 的使用者資料庫帳密提供 Zetaris**。先啟動新的 App API，再執行：

```sh
python3 scripts/sync_social_zetaris.py \
  --platform-env /Users/linziqiao/zetaris-local/zetaris-platform/.env --register
```

腳本會讀 `GET /social/published`，交易更新獨立的 `social_place_records`，只授權 `travel_reader` 讀 `travel_social_places` view；註冊缺少的表、清除這張表的引擎快取，再核對 ID。原始貼文全文、未發布草稿不會同步；已發布的引句與授權照片中繼資料會同步。公開同步請先確認你有權展示這些內容。

後端開啟：

```sh
Zetaris__SocialEnabled=true \
Zetaris__EnvironmentFile=/Users/linziqiao/zetaris-local/zetaris-platform/.env \
  dotnet run --project server-dotnet/SydneyBuddy.Api
```

App 選「透過 Zetaris」後，從 `sydneybuddy_travel.travel_social_places` 與原本 OSM／Overture／Wikivoyage 合併。快照最多快取 15 秒。發布後再執行同步腳本才會反映在 Zetaris；不是即時 CDC。既有部署預設 `SocialEnabled=false`，不會因尚未註冊新表而使舊地圖搜尋失敗。此流程暫以 1,000 篇本機匯入為規模，尚未做全網資料排程或分頁。

AWS 移轉：原有一般資料包不會包含社群紀錄。另用 PostgreSQL 的備份工具搬移 App DB `social_imports` 與公開來源 DB `social_place_records`，在目的端建立／授權 `travel_social_places` view、註冊同名 Zetaris table，再開啟 `Zetaris__SocialEnabled`。不要把包含原始貼文／評論的備份推入 Git；沿用 `infra/zetaris/migration` 的私有憑證與最小權限部署流程。

## 平台接入判斷（2026-10-25 查核）

- [MediaCrawler](https://github.com/NanmiCoder/MediaCrawler)：列出的平台為小紅書、抖音等，沒有列出 Instagram、TikTok、Threads；README 限制商業用途。不能當作四平台的商用共用底座。
- [TikTok Research API](https://developers.tiktok.com/products/research-api/)：需要資格審核，不能預設一般旅遊商業 App 可使用。
- [Instagram Hashtag Search](https://developers.facebook.com/docs/instagram-platform/instagram-api-with-facebook-login/hashtag-search/) 和 [Threads Keyword Search](https://developers.facebook.com/documentation/threads/keyword-search)：後續以你取得的官方 API 權限決定可接範圍；這版未連線驗證。
- Apify SDK 本身與個別 Actor 的授權／價格不同，不能把雲端 Actor 一律視為免費開源。

平台連接器未設定時，不會以測試資料冒充真實抓取結果。這次整合測試的 Test Cafe 僅存在獨立測試 schema，未寫入使用者的景點庫。
