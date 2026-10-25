# 景點搜尋細分類

第一排保留「全部、景點、私房景點、餐飲、住宿、店家、生活設施」，第二排顯示目前大類的細分類；全部模式提供全部細分類作快捷入口。

| 大分類 | 新細分類 |
| --- | --- |
| 景點 | 博物館、公園綠地、海灘 |
| 餐飲 | 餐廳、咖啡廳、甜點烘焙、酒吧夜生活 |
| 住宿 | 飯店旅館、青年旅館 |
| 店家 | 超市雜貨 |
| 生活設施 | 廁所、飲水點 |

細分類使用獨立搜尋 ID，不改行程規劃的既有大分類。OSM／Overture／Zetaris 依真實 poiKind 及來源類型篩選，先篩選再限制筆數，不以店名猜分類。

Apple 原生搜尋使用 MapKit 可用的地點類型；住宿細類以住宿類型搭配 hotel／hostel 關鍵字搜尋。Apple 的 nightlife 涵蓋夜生活地點，不限酒吧。Google 查詢使用對應 Places 類型及既有付費服務政策。平台分類粒度不同，搜尋結果仍以各來源資料為準。

飲水點目前使用開源資料的 drinking_water／water_fountain 標記。Apple 與 Google 模式不以一般店家或水景冒充飲水設施，會顯示切換開源資料的說明。廁所標記也不等於已確認公共開放時間。

本機快照只涵蓋已匯入區域，新增分類不會自行生成缺少的資料。官方分類參考：[Apple MapKit](https://developer.apple.com/documentation/mapkit/mkpointofinterestcategory)、[Google Places](https://developers.google.com/maps/documentation/places/web-service/place-types)。
