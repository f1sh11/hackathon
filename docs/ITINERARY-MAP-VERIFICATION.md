# 首頁行程地圖本地驗證

日期：2026-10-23。分支：`feat/itinerary-map-overview`，從本機 `main` (`6db23a7`) 建立。

## 行為

- iOS OSM／Apple 與 Android OSM 主地圖提供起終點、編號景點、帶白色外框的順序連線及方向箭頭。Google 地圖亦更新編號與圖層呈現，但本機沒有有效 Google Maps SDK key，未驗證實際 Google 底圖。
- 起終點相同時共用「起/終」。20 公尺內的景點共用如「3/4」標記，保留各站與時間；行程與路線仍包含所有站點。
- 首頁站點列可水平捲動；「展開地圖」提供不被天氣卡片遮住的路線總覽。
- 「顯示步行路線」僅在手動點按時呼叫既有導航 API。成功後顯示沿道路的連續路線、總距離與時間、各段步行分鐘。全螢幕視角包含實際路徑的繞行範圍。
- 預覽保存在目前畫面狀態，不寫入行程或導航進度；切換日期／行程清除舊預覽。離線示意圖不聲稱是真實道路線。
- 請求失敗仍顯示順序示意且可重試。預覽不啟動定位、語音或導航。
- 保留 Google live 行程的來源隔離，沒有新增 Google/NVIDIA 付費服務呼叫。地圖圖磚與公開測試路由仍需要網路，不代表離線導航或不受限制的正式上線服務。

## 驗證結果

- Android：編譯、17 個單元測試及 Lint 通過。
- Android API 37 模擬器：新地圖操作測試通過，涵蓋首頁順序列、展開／關閉地圖、OSM 圖磚載入、實際路線預覽、分段時間、保存行程不變、錄製 polyline6 解析、連線失敗回復。一次完整測試對目前開發路由發出一筆路線請求。
- Android：既有 7 個非 LargeTest 操作／導航回歸測試通過。公開路由 smoke test 未重複執行。
- iOS iPhone 17 Pro／iOS 26.5：6 個 XCTest 通過，包含新 polyline6 解析／錯誤資料拒絕、穩定地圖識別碼、既有 Ferrostar 到站及進度測試；最終版本編譯通過。
- iOS 人工確認：首頁及完整地圖顯示編號與方向、相鄰景點「3/4」、實際全天約 3,692 公尺／52 分鐘路線及各段分鐘。此為步行時間，不含景點停留；各段向上取整後相加可能與總分鐘不同。

## 修正的顯示問題

- iOS 以排序 JSON 編碼產生穩定的地圖識別碼，避免刷新時重設預覽。
- OSM 圖磚放在道路層級，行程線明確放在標籤上方；線條加入白色外框提高對比。
- iOS 在畫面佈局完成後調整視角，避免重複邊距造成過度縮小。
- Android 更新路線不再將已載入的底圖狀態永久重設為載入中。

## 重跑

依 README 設定後端與 `Navigation__ValhallaUrl`，並確認 `/health` 的 `navigationConfigured=true`。本機使用既有 Node TLS relay 連接開發路由；正式環境仍需部署可用的路由服務。

```sh
cd android
./gradlew :app:assembleDebug :app:testDebugUnitTest :app:lintDebug
./gradlew :app:connectedDebugAndroidTest \
  -Pandroid.testInstrumentationRunnerArguments.class=com.sydneybuddy.travel.ItineraryOverviewUiTest
./gradlew :app:connectedDebugAndroidTest \
  -Pandroid.testInstrumentationRunnerArguments.notAnnotation=androidx.test.filters.LargeTest
```

本機 Gradle build output 透過工作區 init script 放在暫存目錄，以避免原專案同步目錄的重複建置產物。iOS 可在 Xcode 選擇 `SydneyBuddy` scheme 執行測試。

本次未進行戶外 GPS 或有效金鑰的 Google Maps 實測；沒有推送遠端或合併回 main。
