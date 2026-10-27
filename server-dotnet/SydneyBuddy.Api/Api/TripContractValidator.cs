using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 在保存前检查客户端依赖的行程结构；只校验快照，不重新调用 AI 或地图。
/// Validates the persisted itinerary shape without rerunning paid providers or changing the draft.
/// </summary>
public static class TripContractValidator
{
    public static void Validate(JsonObject trip)
    {
        Strings(trip, "title", "mode", "explanation", "createdAt");
        if (trip.Str("mode") is not ("demo" or "assisted" or "live")) Invalid();
        var profile = Object(trip, "profile");
        Strings(profile, "date");
        Numbers(profile, "days", "budget", "maxSteps", "startMinute", "endMinute");
        StringArray(profile, "interests"); StringArray(profile, "mustVisit");
        Point(Object(profile, "start")); Point(Object(profile, "end"));
        var days = Array(trip, "days");
        if (days.Count is < 1 or > 15 || days.Count != profile.Int("days")) Invalid();
        foreach (var node in days)
        {
            var day = node as JsonObject ?? throw Failure();
            Strings(day, "date", "finishTime"); Numbers(day, "estimatedCost", "estimatedSteps");
            StringArray(day, "warnings"); Point(Object(day, "start")); Point(Object(day, "end"));
            var stops = Array(day, "stops");
            if (stops.Count > 30 || (trip.Object("schedule") is null && stops.Count == 0 && !day.Bool("freeTime"))) Invalid();
            foreach (var item in stops)
            {
                var stop = item as JsonObject ?? throw Failure();
                Point(stop); Strings(stop, "id", "category", "arrival", "departure", "note");
                Numbers(stop, "visitMinutes", "cost", "steps"); Booleans(stop, "indoor", "hoursVerified");
            }
            var legs = Array(day, "legs");
            if (legs.Count != stops.Count + 1) Invalid();
            foreach (var item in legs)
            {
                var leg = item as JsonObject ?? throw Failure();
                Numbers(leg, "meters", "minutes"); Strings(leg, "polyline", "source");
            }
        }
        var weather = Object(trip, "weather");
        Strings(weather, "summary", "source"); Booleans(weather, "rainy");
        StringArray(trip, "warnings");
        foreach (var node in Array(trip, "trace"))
            Strings(node as JsonObject ?? throw Failure(), "title", "detail", "source", "fetchedAt");
    }

    private static void Point(JsonObject point) { Strings(point, "name"); Numbers(point, "lat", "lng"); }
    private static JsonObject Object(JsonObject value, string name) => value[name] as JsonObject ?? throw Failure();
    private static JsonArray Array(JsonObject value, string name) => value[name] as JsonArray ?? throw Failure();
    private static void StringArray(JsonObject value, string name)
    {
        foreach (var node in Array(value, name))
            if (node is not JsonValue scalar || !scalar.TryGetValue<string>(out _)) Invalid();
    }
    private static void Strings(JsonObject value, params string[] names)
    {
        foreach (var name in names)
            if (value[name] is not JsonValue scalar || !scalar.TryGetValue<string>(out _)) Invalid();
    }
    private static void Numbers(JsonObject value, params string[] names)
    {
        foreach (var name in names)
            if (value[name] is not JsonValue scalar || scalar.GetValueKind() != JsonValueKind.Number ||
                !double.TryParse(scalar.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
                !double.IsFinite(number)) Invalid();
    }
    private static void Booleans(JsonObject value, params string[] names)
    {
        foreach (var name in names)
            if (value[name] is not JsonValue scalar || !scalar.TryGetValue<bool>(out _)) Invalid();
    }
    private static JsonException Failure() => new("请求中的行程结构不完整");
    private static void Invalid() => throw Failure();
}
