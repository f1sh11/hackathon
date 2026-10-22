using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>按悉尼行程日期匹配 Google 天气；preserves per-day forecasts and explicitly marks missing data as unknown.</summary>
public sealed class WeatherService(IExternalJsonClient external)
{
    public const string Source = "Google Maps · Weather API";

    public async Task<JsonObject> GetAsync(JsonObject profile, string? key, CancellationToken ct = default)
    {
        var start = DateOnly.ParseExact(profile.Str("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dates = Enumerable.Range(0, profile.Int("days", 1))
            .Select(index => start.AddDays(index).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray();
        var value = new JsonObject
        {
            ["source"] = Source, ["rainy"] = false, ["status"] = "unavailable", ["days"] = new JsonArray(),
            ["fetchedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        try
        {
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException();
            const string url = "https://weather.googleapis.com/v1/forecast/days:lookup?" +
                               "location.latitude=-33.8688&location.longitude=151.2093&" +
                               "days=10&pageSize=10&languageCode=zh-CN&unitsSystem=METRIC";
            var raw = await external.FetchAsync(url, headers: new Dictionary<string, string> { ["X-Goog-Api-Key"] = key },
                timeoutSeconds: 8, cancellationToken: ct);
            var records = new Dictionary<string, JsonObject>();
            foreach (var node in raw["forecastDays"] as JsonArray ?? throw new InvalidDataException())
            {
                var row = node as JsonObject ?? throw new InvalidDataException();
                var date = row["displayDate"] as JsonObject ?? throw new InvalidDataException();
                var day = new DateOnly(date.Int("year"), date.Int("month"), date.Int("day"));
                records[day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = row;
            }

            var forecasts = new JsonArray();
            var availableCount = 0;
            var anyRain = false;
            foreach (var date in dates)
            {
                if (!records.TryGetValue(date, out var row))
                {
                    forecasts.Add(new JsonObject
                    {
                        ["date"] = date, ["summary"] = "超出未来 10 天预报范围", ["rainy"] = false, ["available"] = false
                    });
                    continue;
                }
                // 仅使用与旅行时间重叠的昼夜数据；exclude nighttime rain from a daytime-only trip.
                var parts = new List<JsonObject>();
                if (profile.Int("startMinute") < 1140 && profile.Int("endMinute") > 420)
                    parts.Add(row["daytimeForecast"] as JsonObject ?? new JsonObject());
                if (profile.Int("startMinute") < 420 || profile.Int("endMinute") > 1140)
                    parts.Add(row["nighttimeForecast"] as JsonObject ?? new JsonObject());
                var probabilities = parts.Select(part => ReadNumber(part["precipitation"]?["probability"]?["percent"]))
                    .Where(number => number is >= 0 and <= 100).Select(number => number!.Value).ToArray();
                var low = ReadNumber(row["minTemperature"]?["degrees"]);
                var high = ReadNumber(row["maxTemperature"]?["degrees"]);
                // 缺失概率不能当作零降雨；missing probability means unknown, never an assumed sunny day.
                if (low is null || high is null || probabilities.Length == 0) throw new InvalidDataException();
                var probability = probabilities.Max();
                var descriptions = parts.Select(part => part["weatherCondition"]?["description"]?["text"]?.GetValue<string>() ?? "")
                    .Where(text => text.Length > 0).Distinct();
                var rainy = probability >= 60 || parts.Any(part =>
                {
                    var type = part["weatherCondition"]?["type"]?.GetValue<string>() ?? "";
                    return type.Contains("RAIN", StringComparison.Ordinal) || type.Contains("THUNDER", StringComparison.Ordinal);
                });
                var summary = $"{string.Join(" / ", descriptions)} · {Format(low.Value)}–{Format(high.Value)}°C · 降水概率 {Format(probability)}%";
                forecasts.Add(new JsonObject
                {
                    ["date"] = date, ["summary"] = summary, ["rainy"] = rainy, ["available"] = true,
                    ["temperatureMin"] = low.Value, ["temperatureMax"] = high.Value, ["precipitationProbability"] = probability
                });
                availableCount++;
                anyRain |= rainy;
            }
            value["days"] = forecasts;
            value["status"] = availableCount == dates.Length ? "ok" : availableCount > 0 ? "partial" : "out_of_range";
            value["rainy"] = anyRain;
            value["summary"] = string.Join(" / ", forecasts.Select(day => $"{day!.Str("date")[5..]} {day.Str("summary")}"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // 保留明确的不可用状态；weather failure must not masquerade as verified live data.
            value["summary"] = "天气暂不可用，未按晴天处理";
            value["status"] = "unavailable";
            value["rainy"] = false;
            value["days"] = new JsonArray(dates.Select(date => (JsonNode)new JsonObject
            {
                ["date"] = date, ["summary"] = "天气暂不可用，请稍后刷新", ["rainy"] = false, ["available"] = false
            }).ToArray());
        }
        return value;
    }

    private static double? ReadNumber(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
                                                       && double.TryParse(value.ToJsonString(), NumberStyles.Float,
                                                           CultureInfo.InvariantCulture, out var number)
                                                       && double.IsFinite(number) ? number : null;
    private static string Format(double value) => value.ToString("G", CultureInfo.InvariantCulture);
}
