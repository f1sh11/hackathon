using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 从只读目录取得候选地点，live 模式再逐个补齐 Google 坐标/营业时段；不缓存外部响应。
/// Loads catalog candidates and enriches live evidence from Google; provider responses are never cached here.
/// </summary>
public sealed class PlaceService(IExternalJsonClient external, PlaceCatalog catalog,TravelDataContext? travel=null)
{
    public IReadOnlyList<JsonObject> Catalog => catalog.Get();

    public async Task<JsonObject[]> GetCandidatesAsync(PlanningContext context, CancellationToken ct = default)
    {
        var places = catalog.Get().ToArray();
        if (context.Mode != "live" || travel?.Warehouse==true)
        {
            context.Record("读取候选地点", $"{places.Length} 个悉尼候选地点（含具体餐厅）；地点来源见目录，价格、营业窗口与停留时间为规划估算。", travel?.Warehouse==true ? "Zetaris / travel_catalog":"本地示例数据");
            return places;
        }
        var key = context.Config.GetValueOrDefault("GOOGLE_SERVER_KEY");
        if (string.IsNullOrEmpty(key)) throw new ServiceException("尚未配置 Google 服务端密钥。请在电脑的本地设置页完成配置。");
        foreach (var place in places)
        {
            var result = await external.FetchAsync("https://places.googleapis.com/v1/places:searchText", new JsonObject
            {
                ["textQuery"] = place.Str("query"), ["maxResultCount"] = 1, ["languageCode"] = "zh-CN",
                ["locationBias"] = new JsonObject
                {
                    ["circle"] = new JsonObject
                    {
                        ["center"] = new JsonObject { ["latitude"] = -33.8688, ["longitude"] = 151.2093 },
                        ["radius"] = 20000
                    }
                }
            }, new Dictionary<string, string>
            {
                ["X-Goog-Api-Key"] = key,
                ["X-Goog-FieldMask"] = "places.id,places.displayName,places.location,places.regularOpeningHours,places.googleMapsUri"
            }, cancellationToken: ct);
            var item = result.Array("places")?.FirstOrDefault() as JsonObject;
            if (item is null) throw new ServiceException("Google 未返回完整候选信息，无法生成标为实时的方案。");
            var location = item.Object("location");
            if (location?["latitude"] is null || location["longitude"] is null || string.IsNullOrEmpty(item.Str("id")))
                throw new ServiceException("Google 未返回完整候选信息，无法生成标为实时的方案。");
            var latitude = location.Number("latitude", double.NaN);
            var longitude = location.Number("longitude", double.NaN);
            if (!double.IsFinite(latitude) || !double.IsFinite(longitude))
                throw new ServiceException("Google 未返回完整候选信息，无法生成标为实时的方案。");
            var coordinates = new JsonObject { ["lat"] = latitude, ["lng"] = longitude };
            if (RouteService.Distance(place, coordinates) > 1500)
                throw new ServiceException("地点搜索结果与预期地标不匹配，已停止规划。");
            place["lat"] = coordinates.Number("lat");
            place["lng"] = coordinates.Number("lng");
            place["googlePlaceId"] = item.Str("id");
            place["mapsURL"] = item.Str("googleMapsUri");
            var hours = item.Object("regularOpeningHours");
            place["periods"] = hours is null ? null : (hours.Array("periods")?.DeepClone() ?? new JsonArray());
            place["hoursSource"] = hours is null ? "未提供营业时段，使用演示假设" : "Google 常规营业时段；节假日可能变化";
        }
        context.Record("查询地点与营业时段", "Google Places 返回地点坐标与可用常规营业时段；费用仍为估算。", "Google Places API");
        return places;
    }

}
