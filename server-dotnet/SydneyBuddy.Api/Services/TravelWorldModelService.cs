using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// Produces deterministic, explainable travel simulations. It records observations; it never silently rewrites an itinerary.
/// </summary>
public sealed class TravelWorldModelService(WorldRepository worlds)
{
    public async Task<JsonObject> SimulateAsync(string tripId, string requestId, CancellationToken ct = default)
    {
        ValidateId(tripId, "行程编号");
        ValidateId(requestId, "模拟请求编号");
        var (trip, revision) = await worlds.GetTripAsync(tripId, ct);
        var world = await worlds.EnsureAsync(tripId, ct);
        var existing = await worlds.FindRunAsync(world.Id, requestId, ct);
        if (existing is not null) return await worlds.GetRunAsync(existing.Id, ct);

        var run = await worlds.CreateRunAsync(world, requestId, revision, ct);
        await worlds.AppendEventAsync(run, "simulation_started", "simulation:" + requestId, "simulation_engine",
            new JsonObject { ["tripId"] = tripId, ["tripRevision"] = revision, ["source"] = "confirmed_itinerary" }, ct);

        var profile = trip.Object("profile") ?? throw new ServiceException("已保存行程缺少旅行设置，无法模拟。");
        var days = trip.Array("days")?.OfType<JsonObject>().ToArray() ?? [];
        if (days.Length == 0) throw new ServiceException("已保存行程没有可模拟的日期。");
        var rainy = trip.Object("weather")?.Bool("rainy") ?? false;
        var stepLimit = Math.Max(1, profile.Int("maxSteps", 10_000));
        var dailyBudget = Math.Max(0, profile.Number("budget"));
        var totalSteps = 0;
        var totalCost = 0d;
        var transportMinutes = 0;
        var outdoorMinutes = 0;

        for (var dayIndex = 0; dayIndex < days.Length; dayIndex++)
        {
            var day = days[dayIndex];
            var stops = day.Array("stops")?.OfType<JsonObject>().ToArray() ?? [];
            var legs = day.Array("legs")?.OfType<JsonObject>().ToArray() ?? [];
            var daySteps = Math.Max(0, day.Int("estimatedSteps"));
            var dayCost = Math.Max(0, day.Number("estimatedCost"));
            var dayTransport = legs.Sum(x => Math.Max(0, x.Number("minutes")));
            totalSteps += daySteps;
            totalCost += dayCost;
            transportMinutes += (int)Math.Round(dayTransport, MidpointRounding.AwayFromZero);

            await worlds.AppendSnapshotAsync(run, State(dayIndex + 1, "day_started", Time(profile.Int("startMinute", 600)),
                day.Object("start")?.Str("name", "出发点") ?? "出发点", 0, 0, 0, "low", "low",
                stops.FirstOrDefault()?.Str("name") ?? (day.Object("end")?.Str("name") ?? "返回点")), ct);

            var elapsedSteps = 0;
            var elapsedCost = 0d;
            var elapsedTransport = 0;
            for (var index = 0; index < stops.Length; index++)
            {
                var stop = stops[index];
                elapsedSteps = (int)Math.Round(daySteps * ((index + 1d) / Math.Max(1, stops.Length)), MidpointRounding.AwayFromZero);
                elapsedCost += Math.Max(0, stop.Number("cost"));
                elapsedTransport += (int)Math.Round(Math.Max(0, legs.ElementAtOrDefault(index)?.Number("minutes") ?? 0), MidpointRounding.AwayFromZero);
                if (!stop.Bool("indoor")) outdoorMinutes += Math.Max(0, stop.Int("visitMinutes"));
                var energy = Risk(elapsedSteps, stepLimit);
                var weather = rainy && !stop.Bool("indoor") ? "high" : "low";
                await worlds.AppendSnapshotAsync(run, State(dayIndex + 1, "arrived", stop.Str("arrival", Time(profile.Int("startMinute", 600))),
                    stop.Str("name"), elapsedCost, elapsedSteps, elapsedTransport, energy, weather,
                    stops.ElementAtOrDefault(index + 1)?.Str("name") ?? (day.Object("end")?.Str("name") ?? "返回点")), ct);
            }
            await worlds.AppendSnapshotAsync(run, State(dayIndex + 1, "returned", day.Str("finishTime", Time(profile.Int("endMinute", 1080))),
                day.Object("end")?.Str("name", "返回点") ?? "返回点", dayCost, daySteps,
                (int)Math.Round(dayTransport, MidpointRounding.AwayFromZero), Risk(daySteps, stepLimit), "low", "本日模拟完成"), ct);
        }

        var summary = Summary(totalSteps, totalCost, transportMinutes, outdoorMinutes, stepLimit, dailyBudget * days.Length, rainy);
        var suggestions = Suggestions(summary);
        await worlds.AppendSnapshotAsync(run, new JsonObject
        {
            ["day"] = days.Length, ["eventType"] = "simulation_completed", ["time"] = "",
            ["location"] = "行程模拟完成", ["spent"] = totalCost, ["steps"] = totalSteps,
            ["transportMinutes"] = transportMinutes, ["energyRisk"] = summary.Str("energyRisk"),
            ["weatherRisk"] = summary.Str("weatherRisk"), ["nextStop"] = "查看优化建议",
            ["summary"] = summary, ["suggestions"] = suggestions
        }, ct);
        await worlds.AppendEventAsync(run, "simulation_completed", "simulation_complete:" + requestId, "simulation_engine",
            new JsonObject { ["snapshotCount"] = days.Sum(x => (x.Array("stops")?.Count ?? 0) + 2) + 1, ["source"] = "deterministic_rules" }, ct);
        await worlds.CompleteRunAsync(run, ct);
        return await worlds.GetRunAsync(run.Id, ct);
    }

    public async Task<JsonObject> DecideAsync(string runId, string suggestionId, string decision, CancellationToken ct = default)
    {
        ValidateId(runId, "模拟编号");
        ValidateId(suggestionId, "建议编号");
        if (decision is not ("accepted" or "rejected")) throw new ServiceException("决定必须是接受或拒绝。");
        var rendered = await worlds.GetRunAsync(runId, ct);
        if (!(rendered.Array("suggestions")?.OfType<JsonObject>().Any(x => x.Str("id") == suggestionId) ?? false))
            throw new ServiceException("这条建议不属于本次模拟。");
        var run = new WorldRunEntity { Id = runId, WorldId = rendered.Str("worldId"), RequestId = "", Kind = "", TripRevision = 0, Status = "", CreatedAt = "" };
        await worlds.AppendEventAsync(run, "suggestion_decision", "decision:" + runId + ":" + suggestionId,
            "user", new JsonObject { ["suggestionId"] = suggestionId, ["decision"] = decision, ["preserveUserChoice"] = decision == "rejected" }, ct);
        return await worlds.GetRunAsync(runId, ct);
    }

    private static JsonObject State(int day, string eventType, string time, string location, double spent, int steps, int transport,
        string energyRisk, string weatherRisk, string nextStop) => new()
    {
        ["day"] = day, ["eventType"] = eventType, ["time"] = time, ["location"] = location,
        ["spent"] = Math.Round(spent, 2), ["steps"] = steps, ["transportMinutes"] = transport,
        ["energyRisk"] = energyRisk, ["weatherRisk"] = weatherRisk, ["nextStop"] = nextStop
    };

    private static JsonObject Summary(int steps, double cost, int transportMinutes, int outdoorMinutes, int stepLimit, double totalBudget, bool rainy)
    {
        var energy = Risk(steps, stepLimit);
        var budget = totalBudget <= 0 && cost > 0 ? "high" : Risk(cost, Math.Max(1, totalBudget));
        var weather = rainy && outdoorMinutes > 0 ? "high" : "low";
        return new JsonObject
        {
            ["walkingSteps"] = steps, ["cost"] = Math.Round(cost, 2), ["transportMinutes"] = transportMinutes,
            ["outdoorMinutes"] = outdoorMinutes, ["energyRisk"] = energy, ["budgetRisk"] = budget,
            ["weatherRisk"] = weather, ["before"] = Metrics(steps, cost, transportMinutes, outdoorMinutes),
            ["after"] = Metrics(energy == "high" ? (int)Math.Round(steps * .78) : steps,
                budget == "high" ? cost * .85 : cost, transportMinutes >= 120 ? (int)Math.Round(transportMinutes * .72) : transportMinutes,
                weather == "high" ? (int)Math.Round(outdoorMinutes * .35) : outdoorMinutes)
        };
    }

    private static JsonObject Metrics(int steps, double cost, int transport, int outdoor) => new()
    {
        ["walkingSteps"] = steps, ["cost"] = Math.Round(cost, 2), ["transportMinutes"] = transport, ["outdoorMinutes"] = outdoor
    };

    private static JsonArray Suggestions(JsonObject summary)
    {
        var items = new JsonArray();
        if (summary.Str("weatherRisk") == "high") items.Add(Suggestion("weather", "把户外活动提前", "下雨情景下仍有户外停留，建议优先安排室内地点或上午完成户外活动。"));
        if (summary.Str("energyRisk") == "high") items.Add(Suggestion("energy", "减少连续步行并加入休息", "预计步数接近或超过你的体力上限，建议删减可选站点或增加休息。"));
        if (summary.Str("budgetRisk") == "high") items.Add(Suggestion("budget", "控制可选支出", "估算花费超过本次活动预算，建议保留必去地点并替换可选付费项目。"));
        if (summary.Int("transportMinutes") >= 120) items.Add(Suggestion("transport", "减少折返和交通时间", "当天通勤时间偏长，建议调整相邻景点顺序。"));
        if (items.Count == 0) items.Add(Suggestion("keep", "保留当前安排", "四项指标处于可执行范围；你仍可按个人偏好自行调整。"));
        return items;
    }

    private static JsonObject Suggestion(string id, string title, string detail) => new()
    {
        ["id"] = id, ["title"] = title, ["detail"] = detail, ["status"] = "pending"
    };

    private static string Risk(double value, double limit) => (value / Math.Max(1, limit)) switch
    {
        > .9 => "high", > .65 => "medium", _ => "low"
    };

    private static string Time(int minutes) => $"{Math.Clamp(minutes, 0, 1439) / 60:00}:{Math.Clamp(minutes, 0, 1439) % 60:00}";

    private static void ValidateId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 120 || value.Any(x => !(char.IsLetterOrDigit(x) || x is '-' or '_')))
            throw new ServiceException(label + "不合法。");
    }
}
