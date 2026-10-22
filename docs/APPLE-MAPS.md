# 地图与 Apple Maps

底部「地图」支持自由探索地标，也能读取已保存的行程及历史版本。行程详情的「在地图中查看」会带入正在查看的版本。

## 本版已实现

- 点击示意图编号或站点卡，查看该站时间、剩余 HP 和剩余预算。
- 切换步行、驾车、公交，生成 Apple Maps 官方导航链接。
- 点击「在 Apple 地图查看」，打开对应地名搜索。
- 自行填写出发 / 返程地点后，选择返程站并打开返程导航。
- 无可行解版本明确显示没有路线，不替换成其他版本。
- 未确定地址的餐饮、工作室等活动只提供分类搜索；不虚构具体店铺或坐标。

App 中的插画清楚标注「路线示意 · 非实际道路」。规划器原有 X/Y 是演示网格，没有转换成经纬度。地标名称属于供搜索核对的地点参考，没有完成地理编码或实时营业核验。

## 接入方式

使用 Apple 的 [Unified Maps URLs](https://developer.apple.com/documentation/mapkit/unified-map-urls)：

```text
https://maps.apple.com/search?query=...
https://maps.apple.com/directions?destination=...&mode=walking
```

链接通过 `URL` / `URLSearchParams` 编码，在用户点击时交给 Apple Maps 打开。本版没有内嵌 Apple 底图，没有向 Apple 发送后台 API 请求，也不读取用户定位。真实路线、起点和耗时在 Apple Maps 中确认，不会自动写回或重算演示行程的 HP / 费用。

若后续要在 App 内显示 Apple 底图，可接入 MapKit JS；需要先在 Apple Developer 账户中 [创建 Maps token](https://developer.apple.com/documentation/mapkitjs/creating-a-maps-token)。本版没有配置 token，也没有将链接跳转称为内嵌地图 API。

## 代码与检查

- `client/src/MapScreen.tsx`：地图页与任务 / 版本切换。
- `client/src/appleMaps.ts`：地点参考和官方链接生成。
- `scripts/check-map-links.mjs`：真实 helper 检查，覆盖目录完整性、特殊字符、交通模式、未知地点、未确认活动及返程地址。

使用 Node.js 24：

```sh
node scripts/check-map-links.mjs
```
