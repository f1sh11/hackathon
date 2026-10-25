using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;

namespace SydneyBuddy.Api;

/// <summary>Every adapter returns independent, provider-owned segment geometry. Never join missing shapes.</summary>
public interface IJourneyProvider
{
    string Id { get; }
    string Label { get; }
    string Notice { get; }
    void EnsureAvailable(IReadOnlyList<string> modes);
    Task<IReadOnlyList<JourneyStep>> RouteAsync(JourneyPoint from, JourneyPoint to, string mode, DateTimeOffset departure, CancellationToken ct);
}

internal static class JourneyProviderJson
{
    public static DateTimeOffset Time(JsonNode? node, string key) => DateTimeOffset.TryParse(node.Str(key), CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
        ? value : throw new ServiceException("交通服务未提供有效班次时间。");
    public static double Duration(string raw) => double.TryParse(raw.TrimEnd('s'), CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0 && value < 100_000_000
        ? value : throw new ServiceException("路线时间不完整。");
    public static string? Text(string raw) => string.IsNullOrWhiteSpace(raw) ? null : raw;
    public static async Task<JsonObject> Send(HttpClient client, HttpRequestMessage request, string label, CancellationToken ct)
    {
        try
        {
            using var response = await client.SendAsync(request, ct);
            // Do not expose provider bodies, credentials or request URLs in application errors.
            if (!response.IsSuccessStatusCode) throw new ServiceException($"{label} 查询失败，请检查服务权限、配额及覆盖范围。");
            return await response.Content.ReadFromJsonAsync<JsonObject>(ct) ?? throw new ServiceException($"{label} 回传空资料。");
        }
        catch (HttpRequestException) { throw new ServiceException($"无法连接 {label}，请稍后重试。"); }
        catch (JsonException) { throw new ServiceException($"{label} 回传无效资料。"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ServiceException($"{label} 查询超时，请稍后重试。"); }
    }
}

public sealed class GoogleJourneyProvider(HttpClient client, SecretStore secrets, PaidServicePolicy policy) : IJourneyProvider
{
    public string Id => "google";
    public string Label => "Google Routes";
    public string Notice => "路线由 Google 提供，仅显示于 Google 地图；公共交通时间依服务回传，不标示为 GTFS-RT 实时资料。";
    public void EnsureAvailable(IReadOnlyList<string> modes)
    {
        policy.RequireAllowed();
        if (string.IsNullOrWhiteSpace(secrets.Read()["GOOGLE_SERVER_KEY"])) throw new ServiceException("Google Routes 尚未设置服务端密钥。");
    }
    public async Task<IReadOnlyList<JourneyStep>> RouteAsync(JourneyPoint from, JourneyPoint to, string mode, DateTimeOffset departure, CancellationToken ct)
    {
        EnsureAvailable([mode]);
        JsonObject Location(JourneyPoint p) => new() { ["location"] = new JsonObject { ["latLng"] = new JsonObject { ["latitude"] = p.Lat, ["longitude"] = p.Lng } } };
        var payload = new JsonObject { ["origin"] = Location(from), ["destination"] = Location(to), ["travelMode"] = mode switch { "walk" => "WALK", "drive" => "DRIVE", _ => "TRANSIT" }, ["polylineQuality"] = "HIGH_QUALITY" };
        if (mode == "transit") payload["departureTime"] = departure.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://routes.googleapis.com/directions/v2:computeRoutes") { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-Goog-Api-Key", secrets.Read()["GOOGLE_SERVER_KEY"]);
        request.Headers.Add("X-Goog-FieldMask", "routes.duration,routes.distanceMeters,routes.polyline.encodedPolyline,routes.legs.steps.travelMode,routes.legs.steps.staticDuration,routes.legs.steps.distanceMeters,routes.legs.steps.polyline.encodedPolyline,routes.legs.steps.transitDetails");
        return Parse(await JourneyProviderJson.Send(client, request, Label, ct), from, to, mode, departure);
    }
    public static IReadOnlyList<JourneyStep> Parse(JsonObject body, JourneyPoint from, JourneyPoint to, string mode, DateTimeOffset departure)
    {
        var route = body.Array("routes")?.FirstOrDefault() ?? throw new ServiceException("Google 没有找到此区间的路线。");
        if (mode != "transit")
        {
            var seconds = JourneyProviderJson.Duration(route.Str("duration"));
            var geometry = JourneyService.Decode(route.Object("polyline").Str("encodedPolyline"), 1e5);
            if (geometry.Count < 2) throw new ServiceException("Google 未提供道路路线线形。");
            return [new(mode, null, null, from.Name, to.Name, departure, departure.AddSeconds(seconds), false, null, route.Number("distanceMeters"), seconds, 0, null, null, geometry)];
        }
        var result = new List<JourneyStep>(); var cursor = departure;
        var rawSteps=(route.Array("legs") ?? []).SelectMany(x => x.Array("steps") ?? []).ToArray();
        for (var index=0;index<rawSteps.Length;index++)
        {
            var step=rawSteps[index];
            var nextTransit=rawSteps.Skip(index+1).FirstOrDefault(x => x.Str("travelMode") == "TRANSIT");
            var walkingDestination=nextTransit.Object("transitDetails").Object("stopDetails").Object("departureStop").Str("name",to.Name);
            var detail = step.Object("transitDetails"); var stops = detail.Object("stopDetails");
            var line = detail.Object("transitLine");
            var vehicle = line.Object("vehicle").Str("type");
            var stepMode = step.Str("travelMode") == "WALK" ? "walk" : step.Str("travelMode") == "TRANSIT" ? vehicle switch
            { "BUS" or "INTERCITY_BUS" or "TROLLEYBUS" => "bus", "SUBWAY" or "METRO_RAIL" => "metro", "TRAM" or "LIGHT_RAIL" => "tram", "FERRY" => "ferry", "RAIL" or "HEAVY_RAIL" or "COMMUTER_TRAIN" or "HIGH_SPEED_TRAIN" or "LONG_DISTANCE_TRAIN" => "rail", _ => "transit" } : throw new ServiceException("Google 路线包含不支持的交通方式。");
            var seconds = JourneyProviderJson.Duration(step.Str("staticDuration"));
            var start = stepMode == "walk" ? cursor : JourneyProviderJson.Time(stops, "departureTime");
            var end = stepMode == "walk" ? start.AddSeconds(seconds) : JourneyProviderJson.Time(stops, "arrivalTime");
            if (start < cursor || end < start) throw new ServiceException("Google 班次时间不连续，请重新查询。");
            result.Add(new(stepMode, JourneyProviderJson.Text(line.Str("nameShort", line.Str("name"))), JourneyProviderJson.Text(detail.Str("headsign")),
                stops.Object("departureStop").Str("name", result.Count == 0 ? from.Name : result[^1].To), stops.Object("arrivalStop").Str("name", stepMode == "walk" ? walkingDestination : to.Name),
                start, end, false, detail?["stopCount"] is null ? null : detail.Int("stopCount"), step.Number("distanceMeters"), (end-start).TotalSeconds, (start-cursor).TotalSeconds, null,
                JourneyProviderJson.Text(line.Array("agencies")?.FirstOrDefault().Str("name") ?? ""), JourneyService.Decode(step.Object("polyline").Str("encodedPolyline"), 1e5)));
            cursor = end;
        }
        if (result.Count == 0 || result.All(x => x.Mode == "walk")) throw new ServiceException("Google 未找到大众运输班次，可改选步行。");
        return result;
    }
}

/// <summary>TfNSW Trip Planner supplies GTFS-derived journeys; street-only legs use explicitly labelled Valhalla.</summary>
public sealed class NswJourneyProvider(HttpClient client, SecretStore secrets, NavigationService roads) : IJourneyProvider
{
    public string Id => "nsw";
    public string Label => "Transport for NSW / Trip Planner";
    public string Notice => "公共交通：Transport for NSW；独立步行／开车段：OSM / Valhalla。仅服务明确回传实时估计时标示实时。";
    public void EnsureAvailable(IReadOnlyList<string> modes)
    {
        if (modes.Contains("transit") && string.IsNullOrWhiteSpace(secrets.Read()["TFNSW_API_KEY"])) throw new ServiceException("NSW 交通服务尚未设置，请在本机设置页填写获授权的 Trip Planner API key。不会使用模拟公车替代。");
        if (modes.Any(x => x != "transit") && !roads.Configured) throw new ServiceException("NSW 混合路线的道路服务尚未启用。");
    }
    public async Task<IReadOnlyList<JourneyStep>> RouteAsync(JourneyPoint from, JourneyPoint to, string mode, DateTimeOffset departure, CancellationToken ct)
    {
        EnsureAvailable([mode]);
        if (mode != "transit")
        {
            var response = await roads.PreviewAsync(new JsonObject { ["locations"] = new JsonArray(new JsonObject { ["lat"] = from.Lat, ["lon"] = from.Lng }, new JsonObject { ["lat"] = to.Lat, ["lon"] = to.Lng }) }, mode, ct);
            var route = response.Array("routes")![0]!; var seconds = route.Number("duration");
            var geometry = JourneyService.Decode(route.Str("geometry"), 1e6);
            if (geometry.Count < 2) throw new ServiceException("道路服务未提供线形。");
            return [new(mode, null, null, from.Name, to.Name, departure, departure.AddSeconds(seconds), false, null, route.Number("distance"), seconds, 0, null, null, geometry)];
        }
        var local = TimeZoneInfo.ConvertTime(departure, TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney"));
        string Coordinate(JourneyPoint p) => FormattableString.Invariant($"{p.Lng}:{p.Lat}:EPSG:4326");
        var url = QueryHelpers.AddQueryString("https://api.transport.nsw.gov.au/v1/tp/trip", new Dictionary<string,string?> {
            ["outputFormat"]="rapidJSON", ["coordOutputFormat"]="EPSG:4326", ["depArrMacro"]="dep", ["itdDate"]=local.ToString("yyyyMMdd"), ["itdTime"]=local.ToString("HHmm"),
            ["type_origin"]="coord", ["name_origin"]=Coordinate(from), ["type_destination"]="coord", ["name_destination"]=Coordinate(to), ["TfNSWTR"]="true", ["calcNumberOfTrips"]="1" });
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", "apikey " + secrets.Read()["TFNSW_API_KEY"]);
        return Parse(await JourneyProviderJson.Send(client, request, "NSW Trip Planner", ct), departure);
    }
    public static IReadOnlyList<JourneyStep> Parse(JsonObject body, DateTimeOffset departure)
    {
        var legs = body.Array("journeys")?.FirstOrDefault().Array("legs");
        if (legs is null || legs.Count == 0) throw new ServiceException("NSW 此区间或日期没有可用班次。");
        var result = new List<JourneyStep>(); var cursor = departure;
        foreach (var leg in legs)
        {
            var transport = leg.Object("transportation"); var product = transport.Object("product");
            if (transport.Int("iconId") == 23 || product.Str("name").Contains("demand", StringComparison.OrdinalIgnoreCase)) throw new ServiceException("此班次需要预约，无法作为普通公车安排。");
            var mode = product.Int("class", -1) switch { 1 => "rail", 2 => "metro", 4 => "tram", 5 or 7 or 11 => "bus", 9 => "ferry", 100 => "walk", _ => product.Str("name").ToLowerInvariant() == "footpath" ? "walk" : "" };
            if (mode.Length == 0) throw new ServiceException("NSW 回传尚未支持的交通方式。");
            var origin = leg.Object("origin"); var destination = leg.Object("destination");
            var start = JourneyProviderJson.Time(origin, string.IsNullOrEmpty(origin.Str("departureTimeEstimated")) ? "departureTimePlanned" : "departureTimeEstimated");
            var end = JourneyProviderJson.Time(destination, string.IsNullOrEmpty(destination.Str("arrivalTimeEstimated")) ? "arrivalTimePlanned" : "arrivalTimeEstimated");
            // NSW requests have minute resolution; only a sub-minute initial walking overlap may be clamped.
            if (result.Count == 0 && mode == "walk" && start < cursor && cursor-start < TimeSpan.FromMinutes(1)) start = cursor;
            if (start < cursor || end < start) throw new ServiceException("NSW 班次时间不连续，请重新查询。");
            if (string.IsNullOrWhiteSpace(origin.Str("name")) || string.IsNullOrWhiteSpace(destination.Str("name"))) throw new ServiceException("NSW 上下车站资料不完整。");
            var geometry = new List<JourneyCoordinate>();
            foreach (var point in leg.Array("coords") ?? [])
            {
                if (point is not JsonArray a || a.Count < 2 || a[0] is not JsonValue av || !av.TryGetValue<double>(out var lat) || a[1] is not JsonValue bv || !bv.TryGetValue<double>(out var lng)
                    || !double.IsFinite(lat) || !double.IsFinite(lng) || lat is < -90 or > 90 || lng is < -180 or > 180) throw new ServiceException("NSW 路线坐标不合法。");
                geometry.Add(new(lat,lng));
            }
            result.Add(new(mode, mode == "walk" ? null : JourneyProviderJson.Text(transport.Str("disassembledName", transport.Str("number"))), JourneyProviderJson.Text(transport.Object("destination").Str("name")), origin.Str("name"), destination.Str("name"),
                start, end, leg.Bool("isRealtimeControlled") && (!string.IsNullOrEmpty(origin.Str("departureTimeEstimated")) || !string.IsNullOrEmpty(destination.Str("arrivalTimeEstimated"))),
                null, leg.Number("distance"), (end-start).TotalSeconds, (start-cursor).TotalSeconds, null, JourneyProviderJson.Text(transport.Object("operator").Str("name")), geometry));
            cursor = end;
        }
        if (result.All(x => x.Mode == "walk")) throw new ServiceException("NSW 未找到大众运输班次，可改选步行。");
        return result;
    }
}
