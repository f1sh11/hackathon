# 多站步行導航驗證（2026-10-23）

分支：`feat/multi-stop-navigation`，從 `main` 的 `3ab0c2e` 建立。此功能使用 Ferrostar Core 0.59.0，行程進度由 App 管理，沒有改寫已保存的行程，也沒有接入 Google Navigation SDK。

## 已驗證

| 範圍 | 結果 |
|---|---|
| ASP.NET Core | 74 項測試通過：原有行程 / 資料庫流程、導航服務未配置、座標與數量驗證、多站順序、固定步行配置、錯誤與取消請求 |
| Android 單元測試 | 13 項通過：原有領域邏輯、底圖政策，以及多站順序、返回段、損壞進度和行程版本隔離 |
| Android 裝置 / UI 測試 | 8 項通過：原有完整行程流程、底圖切換、真正 Ferrostar 路線回放到站、失敗時保留進度、零距離到站、請求中暫停，以及預覽 / 到站等待 / 下一站操作；另含一項真實首頁與路線服務連線測試 |
| Android Lint | 通過，0 errors；仍有套件更新等非阻斷警告。對未註冊 / 未啟動的 Ferrostar 選用通知服務，僅忽略該類別的通知權限誤報，說明見 `android/app/lint.xml` |
| iOS XCTest | 4 項通過：多站進度、模擬與真實進度隔離 / 進度鍵穩定、零距離到站，以及真正 Ferrostar 的悉尼路線解析與回放到站 |
| iOS 回放穩定性 | 改用 10 倍模擬後，同一條真實路線到站測試連續 3 次通過；不以重試掩蓋失敗 |
| 後端真實連線 | 經 `POST /navigation/route` 取得環形碼頭 → 歌劇院 → 環形碼頭的 2 段路線，約 1,358 公尺 |
| iOS 手動操作 | 整天 5 段預覽約 3,692 公尺；依序走完植物園、歌劇院、岩石區、美術館與返程，確認到站停留和完成清單；到站時由 OSM 切到 Apple Maps 保留進度 |

Android 裝置為 Android 17 / API 37 的 Medium Phone 模擬器；iOS 為 iOS 26.5 的 iPhone 17 Pro 模擬器。自動導航回放測試讀取 `tests/fixtures/walking-sydney.json`，使用真正 SDK 引擎和模擬位置，不反覆呼叫公共路由服務。Android UI 測試需要本機後端啟動並回報 `navigationConfigured=true`。`WalkingLiveSmokeTest` 標為 LargeTest，使用少量真實路線請求並自動清理其臨時行程；其餘導航回放使用錄製資料。真實測試也確認導航不修改行程內容。

## 測試服務與成本

本機 .NET 對公共 Valhalla 展示站出現 TLS 握手不相容；已用 `scripts/navigation-demo-proxy.mjs` 經 Node 24 正常驗證 TLS 後轉接，後端設為 `Navigation__ValhallaUrl=http://127.0.0.1:8002/route`。此轉接器只限本機開發，不是自架路由引擎，不可作營運後端或負載測試服務。

`AllowPaidServices` 保持關閉；測試沒有呼叫 NVIDIA、Google Places、Google Routes 或即時天氣。Google 地圖未提供有效 SDK 金鑰，因此不聲稱已驗證真實 Google 底圖。少量手動路線請求仍使用公共 Valhalla 展示站，圖磚仍使用 OSM 公共服務，兩者均無可用性保證。

## 尚未驗證 / 明確範圍

- 真機戶外 GPS、定位誤差、弱網路下長時間導航、實際偏航重算與系統語音可聽性，仍需實機測試。
- 目前為前台步行導航；離開頁面、切到背景或鎖屏會暫停。不是持續背景導航。
- Android 初次真實連線發現 SDK 在 UI 執行緒讀取回應的問題，已將路線請求與解析移至 IO 執行緒；真實預覽、到站、下一站與全套測試已重跑通過。
- 模擬速度固定 10 倍；高倍速回放曾出現誤判偏航，已降低更新頻率並連續驗證。
- 不含公車 / 火車混合路線、即時交通、停靠順序最佳化、離線路網與商店發布。
- `live` 行程含 Google 來源座標，不進入開源導航；目前使用 `demo` / `assisted` 示例景點驗證。
- 在此本機原型中，實際位置只用於目前路段，不寫入後端行程；到站進度保存在裝置。重新打開後需手動開始本段，從新位置重算。
