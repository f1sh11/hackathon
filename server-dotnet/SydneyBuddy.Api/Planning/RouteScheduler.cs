using System.Globalization;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 用确定性规则选择每天路线：预算、步数、分段营业、衔接/休息、返程及跨天去重。
/// Selects daily routes with deterministic budget, steps, opening-window, rest, return and uniqueness constraints.
/// </summary>
public sealed class RouteScheduler(RouteService routes)
{
    public async Task<JsonArray> ScheduleDaysAsync(JsonObject profile, IReadOnlyList<JsonObject> places,
        JsonObject weather, IReadOnlyDictionary<string, int> ranks, PlanningContext context, CancellationToken ct = default)
    {
        var interests = profile.Array("interests")!.Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var mustVisit = profile.Array("mustVisit")!.Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var days = new JsonArray();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var firstDate = DateOnly.ParseExact(profile.Str("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        for (var day = 0; day < profile.Int("days"); day++)
        {
            ct.ThrowIfCancellationRequested();
            var daily = (JsonObject)profile.DeepClone();
            daily["date"] = firstDate.AddDays(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            // 每一天使用自己的天气；某一天有雨不能把其他晴天的户外景点一并过滤。
            // Apply rain filtering per date, rather than excluding outdoor places for the entire trip.
            var forecast = weather.Array("days")?.OfType<JsonObject>().FirstOrDefault(x => x.Str("date") == daily.Str("date"));
            var rainy = forecast?.Bool("rainy") ?? weather.Bool("rainy");
            var pool = places.Where(x => !used.Contains(x.Str("id")) &&
                (!rainy || x.Bool("indoor") || mustVisit.Contains(x.Str("id")))).ToArray();
            HashSet<string> required = day == profile.Int("days") - 1
                ? mustVisit.Except(used).ToHashSet(StringComparer.Ordinal) : [];
            var dailyLimit = Math.Min(4, (int)Math.Ceiling((double)pool.Length / (profile.Int("days") - day)));
            var options = new List<(double Score, JsonObject[] Order)>();

            for (var length = 1; length <= dailyLimit; length++)
            {
                foreach (var order in Permutations(pool, length))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!required.IsSubsetOf(order.Select(x => x.Str("id")))) continue;
                    var estimate = await ScheduleAsync(order, daily, context, exact: false, ct);
                    if (estimate is null) continue;
                    var score = order.Sum(point => 10d + (interests.Contains(point.Str("category")) ? 5 : 0)
                        + (ranks.Count - ranks.GetValueOrDefault(point.Str("id"), ranks.Count)) * 2
                        - (rainy && !point.Bool("indoor") ? 14 : 0));
                    score -= estimate.Array("legs")!.OfType<JsonObject>().Sum(x => x.Number("minutes")) * 0.09;
                    score += 50 * order.Count(x => mustVisit.Contains(x.Str("id")));
                    options.Add((score, order));
                }
            }

            JsonObject? selected = null;
            // 只对排名最高的 8 条初筛路线请求实际路段；保留稳定顺序，便于迁移前后比较。
            // Check actual legs for the top eight estimates; stable ties preserve Python's catalog/permutation order.
            foreach (var option in options.OrderByDescending(x => x.Score).Take(8))
            {
                selected = await ScheduleAsync(option.Order, daily, context, exact: true, ct);
                if (selected is not null) break;
            }
            if (selected is null && places.All(p=>used.Contains(p.Str("id"))))
            {
                selected=await ScheduleAsync([],daily,context,exact:true,ct);
                if (selected is not null) {
                    selected["freeTime"]=true;
                    selected.Array("warnings")!.Add("自由安排：现有候选景点已用完，请自行添加地点；当天未安排景点活动。");
                }
            }
            if (selected is null)
                throw new ServiceException($"第 {day + 1} 天没有满足预算、步数、营业时间和返程条件的路线。请增加时间/预算/步数，或减少必去地点。");
            selected["date"] = daily.Str("date");
            days.Add(selected);
            used.UnionWith(selected.Array("stops")!.OfType<JsonObject>().Select(x => x.Str("id")));
        }

        if (!mustVisit.IsSubsetOf(used)) throw new ServiceException("无法安排全部必去地点，请放宽约束。");
        return days;
    }

    private async Task<JsonObject?> ScheduleAsync(IReadOnlyList<JsonObject> order, JsonObject profile, PlanningContext context, bool exact, CancellationToken ct)
    {
        var clock = profile.Int("startMinute");
        var totalSteps = 0;
        var totalCost = 0d;
        var stops = new JsonArray();
        var legs = new JsonArray();
        var warnings = new JsonArray();
        var previous = profile.Object("start")!;
        foreach (var point in order)
        {
            var leg = exact ? await routes.LegAsync(previous, point, context, ct) : RouteService.EstimateLeg(previous, point);
            var steps = (int)Math.Ceiling(leg.Number("meters") / 0.75) + (int)Math.Ceiling(point.Number("visitMinutes") * 12);
            // 预留 10 分钟衔接及每站 15 分钟休息；步数包含场内参观和最后返程。
            // Account for transfer/rest buffers, internal walking, and the final return leg.
            var arrival = clock + (int)leg.Number("minutes") + 10;
            var windows = OpeningWindows(point, profile.Str("date"), out var verified);
            var feasible = windows.Select(x => (Start: Math.Max(arrival, x.Open), x.Close))
                .Where(x => x.Start + point.Int("visitMinutes") <= x.Close).OrderBy(x => x.Start).ThenBy(x => x.Close).ToArray();
            if (feasible.Length == 0) return null;
            var start = feasible[0].Start;
            clock = start + point.Int("visitMinutes") + 15;
            totalSteps += steps;
            totalCost += point.Number("cost");
            if (totalCost > profile.Number("budget") || totalSteps > profile.Int("maxSteps")) return null;
            var stop = CopyFields(point, "id", "name", "lat", "lng", "category", "indoor", "visitMinutes", "cost", "note");
            stop["arrival"] = TimeLabel(start);
            stop["departure"] = TimeLabel(start + point.Int("visitMinutes"));
            stop["hoursVerified"] = verified;
            stop["mapsURL"] = point.Str("mapsURL");
            stop["steps"] = steps;
            stops.Add(stop);
            legs.Add(leg);
            previous = point;
            if (!verified) warnings.Add(point.Str("name") + "：营业时间未核实");
        }
        var back = exact ? await routes.LegAsync(previous, profile.Object("end")!, context, ct) : RouteService.EstimateLeg(previous, profile.Object("end")!);
        totalSteps += (int)Math.Ceiling(back.Number("meters") / 0.75);
        clock += (int)back.Number("minutes") + 10;
        if (clock > profile.Int("endMinute") || totalSteps > profile.Int("maxSteps")) return null;
        legs.Add(back);
        return new JsonObject
        {
            ["stops"] = stops, ["legs"] = legs, ["estimatedCost"] = totalCost, ["estimatedSteps"] = totalSteps,
            ["finishTime"] = TimeLabel(clock), ["warnings"] = warnings,
            ["start"] = profile["start"]!.DeepClone(), ["end"] = profile["end"]!.DeepClone()
        };
    }

    public static IReadOnlyList<(int Open, int Close)> OpeningWindows(JsonObject place, string date, out bool verified)
    {
        if (place["periods"] is null)
        {
            verified = false;
            return [(place.Int("openMinute"), place.Int("closeMinute"))];
        }
        verified = true;
        var weekday = (int)DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayOfWeek;
        var intervals = new List<(int Open, int Close)>();
        // 分段营业时段独立检查；不能把午间闭馆的两个窗口合并成全天开放。
        // Keep split opening windows separate so a midday closure is never treated as open time.
        foreach (var period in place.Array("periods")?.OfType<JsonObject>() ?? [])
        {
            var open = period.Object("open");
            var close = period.Object("close");
            if (open?.Int("day", -1) == 0 && open.Int("hour") == 0 && close is null) return [(0, 1440)];
            if (open?["day"] is null || close is null) continue;
            var start = open.Int("hour") * 60 + open.Int("minute");
            var end = close.Int("hour") * 60 + close.Int("minute");
            if (open.Int("day") == weekday) intervals.Add((start, close.Int("day", -1) == weekday ? end : 1440));
            else if (close.Int("day", -1) == weekday) intervals.Add((0, end));
        }
        return intervals;
    }

    private static string TimeLabel(int minute) => $"{minute / 60:00}:{minute % 60:00}";

    private static JsonObject CopyFields(JsonObject source, params string[] fields)
    {
        var result = new JsonObject();
        foreach (var field in fields) result[field] = source[field]?.DeepClone();
        return result;
    }

    private static IEnumerable<JsonObject[]> Permutations(JsonObject[] pool, int length)
    {
        var used = new bool[pool.Length];
        var current = new JsonObject[length];
        return Enumerate(0);

        IEnumerable<JsonObject[]> Enumerate(int depth)
        {
            if (depth == length)
            {
                yield return (JsonObject[])current.Clone();
                yield break;
            }
            for (var index = 0; index < pool.Length; index++)
            {
                if (used[index]) continue;
                used[index] = true;
                current[depth] = pool[index];
                foreach (var order in Enumerate(depth + 1)) yield return order;
                used[index] = false;
            }
        }
    }
}
