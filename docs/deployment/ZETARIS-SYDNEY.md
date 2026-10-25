# Zetaris 雪梨雲端與本機切換

## 架構

```
iOS / Android → ASP.NET Core
                    ├ direct → 原始公開供應商
                    ├ zetaris → 本機 Zetaris → 本機公開來源 PostgreSQL
                    └ zetaris-cloud → 雲端 HTTPS Zetaris → 雲端 NDP File System / REST
```

App 設定中的資料來源選擇以 `X-Travel-Data-Source` 傳遞。手機不保存 Zetaris 帳密、不發 SQL、不提供任意連線網址。既有 `zetaris` 值代表本機，`zetaris-cloud` 代表雲端。三種來源的回應保留同樣的 `dataSource` 識別，切換時丟棄舊來源的搜尋回應。

雲端不能連線時明確報錯，不靜默改讀本機。靜態集合的短期記憶體快取依本機／雲端、API、登入端點、組織與帳號隔離；天氣每次透過所選來源即時查詢，不讀快取快照。

## 已完成雲端遷移與驗證（2026-10-25）

- 雲端 Zetaris Gateway：`https://new-ui.a6c43f1d-2765-487d-89af-99b612b39de9.cloud.zetaris.com/`
- `sydneybuddy_travel` Lightning Database（`dffs://sydneybuddy-public-20261025/*.json`）：
  - `travel_metadata`: 1 筆
  - `travel_poi`: 5,968 筆
  - `travel_catalog`: 8 筆
  - `travel_map_layers`: 4 筆
  - `travel_hidden_gems`: 12 筆
  - `travel_osm_places`: 5,318 筆
  - `travel_activity_offers`: 16 筆
- `sydneybuddy_live` Lightning Database（即時 REST Data Sources）：
  - `weather_sydney`（Open-Meteo 即時雪梨 7 天預報，無快取）
  - `traffic_nsw`（Transport for NSW Live Traffic Hazards）
  - `fees_sydney`（City of Sydney Fees & Charges）
