using System.Globalization;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 校验并补全Android / iOS 共用的行程请求；不调用外部服务、不写数据库。
/// Validates and normalizes the shared planning request before any provider calls or database writes.
/// </summary>
public static class PlanValidator
{
    private const string InvalidInput = "请检查日期、时间、预算、步数和悉尼范围内的起终点。";

    /// <summary>
    /// 输入原始 JSON 与可用景点，返回独立的规范化副本；错误统一转为可展示的业务异常。
    /// Takes raw JSON and the catalog; returns an independent normalized copy or a user-safe domain error.
    /// </summary>
    public static JsonObject Validate(JsonObject raw, IReadOnlyList<JsonObject> catalog)
    {
        try
        {
            var profile = (JsonObject)raw.DeepClone();
            if(profile["indoorPreference"] is not null&&profile.Str("indoorPreference") is not ("balanced" or "preferIndoor" or "preferOutdoor"))throw new FormatException();
            if(profile["weatherTolerance"] is not null&&profile.Str("weatherTolerance") is not ("standard" or "acceptRain" or "avoidRain"))throw new FormatException();
            if (!DateOnly.TryParseExact(profile.Str("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                throw new FormatException();

            if(profile.Str("transportPreference","walk") is not ("walk" or "balanced" or "lessWalking"))throw new FormatException();
            var budget = ReadNumber(profile, "budget", 100);
            var maxSteps = ReadInteger(profile, "maxSteps", DailyPacing.ReferenceSteps(profile));
            var startMinute = ReadInteger(profile, "startMinute", 600);
            var endMinute = ReadInteger(profile, "endMinute", 1200);
            var days = ReadInteger(profile, "days", 1);
            var speed=ReadNumber(profile,"walkingSpeedKmh",4.5);var stride=ReadNumber(profile,"stepLengthMeters",.75);
            var target=Math.Min(endMinute,ReadInteger(profile,"targetReturnMinute",startMinute>=1200?endMinute:Math.Min(1200,endMinute)));
            if(!double.IsFinite(speed)||speed is <1 or >7||!double.IsFinite(stride)||stride is <.3 or >1.2||target<=startMinute||target>endMinute)throw new FormatException();
            if(profile["energyFeedback"] is not null&&profile.Str("energyFeedback") is not ("tired" or "normal" or "energized"))throw new FormatException();
            if(profile["accommodationStatus"] is not null&&profile.Str("accommodationStatus") is not ("unknown" or "unbooked" or "selected" or "booked"))throw new FormatException();
            if(profile["hotel"] is not null){
                var hotel=profile.Object("hotel")??throw new FormatException();
                if(profile.Str("accommodationStatus") is not ("selected" or "booked"))throw new FormatException();
                if(!profile.ContainsKey("end"))profile["end"]=hotel.DeepClone();
                if(!profile.ContainsKey("start"))profile["start"]=hotel.DeepClone();
            }
            profile["walkingSpeedKmh"]=speed;profile["stepLengthMeters"]=stride;profile["targetReturnMinute"]=target;

            if (!double.IsFinite(budget) || budget is < 0 or > 100000 || maxSteps is < 500 or > 60000 ||
                startMinute < 0 || startMinute >= endMinute || endMinute > 1440 || days is < 1 or > 15)
                throw new FormatException();
            _ = date.AddDays(days - 1); // 校验跨天日期边界。 / Validate the entire date span, including year-end overflow.
            if (Text(profile["request"]).Length > 1500)
                throw new FormatException();

            // 必去景点只能引用现有 ID，防止模型或请求引入无法验证的地点。
            // Required attractions must reference known IDs so every stop can be independently checked.
            var knownIds = catalog.Select(x => x.Str("id")).ToHashSet(StringComparer.Ordinal);
            var mustVisit = new JsonArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (profile.TryGetPropertyValue("mustVisit", out var required))
            {
                if (required is not JsonArray requiredIds)
                    throw new FormatException();
                foreach (var item in requiredIds)
                {
                    if (item is not JsonValue value || !value.TryGetValue<string>(out var id) ||
                        !knownIds.Contains(id))
                        throw new FormatException();
                    if (seen.Add(id)) mustVisit.Add(id);
                }
            }

            var interests = new JsonArray();
            if (profile.TryGetPropertyValue("interests", out var interestNode))
            {
                if (interestNode is not JsonArray source)
                    throw new FormatException();
                foreach (var item in source.Take(6)) interests.Add(Truncate(Text(item), 20));
            }

            foreach (var endpoint in new[] { "start", "end" })
            {
                JsonObject point;
                if (!profile.ContainsKey(endpoint))
                    point = new JsonObject { ["name"] = "环形码头", ["lat"] = -33.8611, ["lng"] = 151.2126 };
                else
                    point = profile[endpoint] as JsonObject ?? throw new FormatException();
                var latitude = ReadNumber(point, "lat", double.NaN);
                var longitude = ReadNumber(point, "lng", double.NaN);
                if (!double.IsFinite(latitude) || !double.IsFinite(longitude) ||
                    latitude is < -37.5 or > -28.0 || longitude is < 141.0 or > 154.0)
                    throw new FormatException();
                profile[endpoint] = new JsonObject
                {
                    ["name"] = Truncate(point.ContainsKey("name") ? Text(point["name"]) : "指定地点", 60),
                    ["lat"] = latitude,
                    ["lng"] = longitude
                };
            }

            if(profile["hotel"] is JsonObject selectedHotel){
                var lat=ReadNumber(selectedHotel,"lat",double.NaN);var lng=ReadNumber(selectedHotel,"lng",double.NaN);
                if(!double.IsFinite(lat)||!double.IsFinite(lng)||lat is < -37.5 or > -28||lng is <141 or >154)throw new FormatException();
                profile["hotel"]=new JsonObject{["name"]=Truncate(Text(selectedHotel["name"]),60),["lat"]=lat,["lng"]=lng};
            }
            RouteLogistics.Normalize(profile);
            MealTiming.Validate(profile);
            profile["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            profile["budget"] = budget;
            profile["maxSteps"] = maxSteps;
            profile["startMinute"] = startMinute;
            profile["endMinute"] = endMinute;
            profile["days"] = days;
            profile["mustVisit"] = mustVisit;
            profile["interests"] = interests;
            return profile;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            throw new ServiceException(InvalidInput);
        }
    }

    public static JsonObject ValidateForecast(JsonObject raw) {
        try {
            if(!DateOnly.TryParseExact(raw.Str("date"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw new FormatException();
            var days=ReadInteger(raw,"days",1);if(days is <1 or >15)throw new FormatException();_ = date.AddDays(days-1);
            return new(){["date"]=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),["days"]=days,["weatherOverride"]=raw.Str("weatherOverride")};
        }catch(Exception e)when(e is FormatException or InvalidOperationException or ArgumentException or OverflowException){throw new ServiceException("請檢查預報日期與天數。");}
    }

    private static double ReadNumber(JsonObject node, string name, double fallback)
    {
        if (!node.ContainsKey(name)) return fallback;
        if (node[name] is not JsonValue value) throw new FormatException();
        if (value.TryGetValue<double>(out var number)) return number;
        if (value.TryGetValue<int>(out var integer)) return integer;
        if (value.TryGetValue<long>(out var longValue)) return longValue;
        if (value.TryGetValue<string>(out var text) &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        throw new FormatException();
    }

    private static int ReadInteger(JsonObject node, string name, int fallback)
    {
        if (!node.ContainsKey(name)) return fallback;
        if (node[name] is JsonValue value && value.TryGetValue<string>(out var text))
            return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return checked((int)ReadNumber(node, name, fallback));
    }

    internal static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text)
        ? text : node?.ToJsonString() ?? "None";
    internal static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
