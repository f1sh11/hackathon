using System.Globalization;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 提供真实/演示步行路段；相同路段只在一次规划上下文中复用，不跨用户或请求缓存。
/// Provides live/demo walking legs; repeated legs are reused only within one planning operation.
/// </summary>
public sealed class RouteService(IExternalJsonClient external,TripTransportRouter? transport=null)
{
    public async Task<JsonObject> TimedLegAsync(JsonObject from,JsonObject to,JsonObject profile,int minute,PlanningContext context,CancellationToken ct) {
        var walk=await LegAsync(from,to,context,ct);
        if(!context.CompareTransit)return walk;
        if(transport is null)throw new ServiceException("公共交通比较服务尚未启动。");
        return await transport.ChooseAsync(from,to,walk,profile,minute,context,ct);
    }
    public async Task<JsonObject> LegAsync(JsonObject from, JsonObject to, PlanningContext context, CancellationToken ct = default)
    {
        if(context.Walking is not null)return context.Walking.Get(from,to);
        var cacheKey = (from.Number("lat"), from.Number("lng"), to.Number("lat"), to.Number("lng"));
        if (context.RouteCache.TryGetValue(cacheKey, out var cached)) return (JsonObject)cached.DeepClone();
        JsonObject route;
        if (context.Mode == "live")
        {
            var response = await external.FetchAsync("https://routes.googleapis.com/directions/v2:computeRoutes", new JsonObject
            {
                ["origin"] = RouteCoordinate(from), ["destination"] = RouteCoordinate(to), ["travelMode"] = "WALK",
                ["computeAlternativeRoutes"] = false, ["languageCode"] = "zh-CN"
            }, new Dictionary<string, string>
            {
                ["X-Goog-Api-Key"] = context.Config["GOOGLE_SERVER_KEY"],
                ["X-Goog-FieldMask"] = "routes.duration,routes.distanceMeters,routes.polyline.encodedPolyline"
            }, cancellationToken: ct);
            try
            {
                var item = response.Array("routes")?.FirstOrDefault() as JsonObject ?? throw new FormatException();
                if (item["distanceMeters"] is null) throw new FormatException();
                var meters = item.Number("distanceMeters", double.NaN);
                var seconds = double.Parse(item.Str("duration").TrimEnd('s'), CultureInfo.InvariantCulture);
                if (!double.IsFinite(meters) || !double.IsFinite(seconds) || meters < 0 || seconds < 0)
                    throw new FormatException();
                route = new JsonObject
                {
                    ["meters"] = meters, ["minutes"] = Math.Ceiling(seconds / 60),
                    ["polyline"] = item.Object("polyline")?.Str("encodedPolyline") ?? "",
                    ["source"] = "Google Routes 步行路线"
                };
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
            {
                throw new ServiceException("路线不可达或 Google Routes 数据不完整。");
            }
        }
        else
        {
            var meters = Math.Ceiling(Distance(from, to) * 1.3);
            route = new JsonObject
            {
                ["meters"] = meters, ["minutes"] = Math.Ceiling(meters / 65),
                ["polyline"] = "", ["source"] = "直线距离×1.3 的演示估算"
            };
        }
        context.RouteCache[cacheKey] = (JsonObject)route.DeepClone();
        return route;
    }

    private static JsonObject RouteCoordinate(JsonObject point) => new()
    {
        ["location"] = new JsonObject
        {
            ["latLng"] = new JsonObject { ["latitude"] = point.Number("lat"), ["longitude"] = point.Number("lng") }
        }
    };

    public static JsonObject EstimateLeg(JsonObject from, JsonObject to) => new()
    {
        ["meters"] = Math.Ceiling(Distance(from, to) * 1.3),
        ["minutes"] = Math.Ceiling(Distance(from, to) * 1.3 / 65), ["polyline"] = "", ["source"] = "初筛估算"
    };

    public static double Distance(JsonObject a, JsonObject b)
    {
        var latitude1 = a.Number("lat") * Math.PI / 180;
        var latitude2 = b.Number("lat") * Math.PI / 180;
        var deltaLatitude = latitude2 - latitude1;
        var deltaLongitude = (b.Number("lng") - a.Number("lng")) * Math.PI / 180;
        var haversine = Math.Pow(Math.Sin(deltaLatitude / 2), 2) +
            Math.Cos(latitude1) * Math.Cos(latitude2) * Math.Pow(Math.Sin(deltaLongitude / 2), 2);
        return 6371000 * 2 * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(Math.Max(0, 1 - haversine)));
    }

}
