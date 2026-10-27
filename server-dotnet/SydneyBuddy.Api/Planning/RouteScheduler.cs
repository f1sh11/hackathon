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
            var dailyRanks=context.AgentDayRanks.GetValueOrDefault(daily.Str("date"))??ranks;
            // 每一天使用自己的天气；某一天有雨不能把其他晴天的户外景点一并过滤。
            // Apply rain filtering per date, rather than excluding outdoor places for the entire trip.
            var forecast = weather.Array("days")?.OfType<JsonObject>().FirstOrDefault(x => x.Str("date") == daily.Str("date"));
            var rainy = forecast?.Bool("rainy") ?? weather.Bool("rainy");
            var available = places.Where(x => !used.Contains(x.Str("id")) &&
                (context.DayCandidateIds is null || context.DayCandidateIds[day].Contains(x.Str("id"))) &&
                (!context.MeterlessPlanning || !rainy || profile.Str("weatherTolerance")!="avoidRain" || x.Bool("indoor")&&(x["indoorKnown"] is null||x.Bool("indoorKnown"))) &&
                (context.MeterlessPlanning || profile.Str("weatherOverride")!="rain" || x.Bool("indoor") || mustVisit.Contains(x.Str("id")))).ToArray();
            // Forecast rain changes preference and outdoor duration, not the entire purpose of a trip.
            var pool=RecommendationQuality.DailyPool(available,daily,mustVisit,dailyRanks).Select(x=> {
                var p=(JsonObject)x.DeepClone();
                if(!context.MeterlessPlanning&&rainy&&!p.Bool("indoor")&&(p["indoorKnown"] is null||p.Bool("indoorKnown"))&&!mustVisit.Contains(p.Str("id"))) {
                    p["visitMinutes"]=Math.Min(p.Int("visitMinutes"),30);
                    p["note"]=p.Str("note")+"；预报有雨，缩短户外停留，出发前确认天气。";
                }
                return p;
            }).ToArray();
            HashSet<string> required = day == profile.Int("days") - 1
                ? mustVisit.Except(used).ToHashSet(StringComparer.Ordinal) : [];
            var relaxed=profile.Str("energy")=="低精力" || profile.Str("rhythm")=="轻松随意";
            var activityLimit=relaxed ? 3:4;
            if(context.DayCandidateIds is null && profile.Int("days")-day>1) {
                // Do not spend all suitable visits on early days and leave only a meal later.
                var visits=available.Count(p=>!RecommendationQuality.IsMeal(p));
                var meal=available.Any(RecommendationQuality.IsMeal) && RecommendationQuality.MealWindow(daily) is not null ? 1:0;
                activityLimit=Math.Min(activityLimit,Math.Max(1,(int)Math.Ceiling((double)visits/(profile.Int("days")-day))+meal));
            }
            var dailyLimit = Math.Min(Math.Max(activityLimit,required.Count),pool.Length);
            if(dailyLimit>5)throw new ServiceException("单日必去地点过多，请分散到多天后再生成。");
            var options = new List<(double Score, JsonObject[] Order, double Travel, int Waiting)>();

            for (var length = 1; length <= dailyLimit; length++)
            {
                foreach (var order in Permutations(pool, length))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!required.IsSubsetOf(order.Select(x => x.Str("id")))) continue;
                    var mealCount=order.Count(RecommendationQuality.IsMeal);
                    if(mealCount>1 || (mealCount>0 && RecommendationQuality.MealWindow(daily) is null))continue;
                    var estimate = await ScheduleAsync(order, daily, context, exact: false, ct);
                    if (estimate is null) continue;
                    var score = order.Sum(point => 10d + RecommendationQuality.InterestScore(point,interests)
                        + (dailyRanks.Count - dailyRanks.GetValueOrDefault(point.Str("id"), dailyRanks.Count)) * 0.8
                        - (rainy && !point.Bool("indoor") && (!context.MeterlessPlanning || profile.Str("weatherTolerance")!="acceptRain") ? 18 : 0)
                        + (context.MeterlessPlanning && (point["indoorKnown"] is null||point.Bool("indoorKnown")) &&
                            (profile.Str("indoorPreference")=="preferIndoor"&&point.Bool("indoor")||profile.Str("indoorPreference")=="preferOutdoor"&&!point.Bool("indoor")) ? 12 : 0));
                    // One real meal plus visits beats either skipping lunch or filling a day with restaurants.
                    if(mealCount>0)score+=35;
                    if(order.Any(p=>!RecommendationQuality.IsMeal(p)))score+=15;
                    score += order.SelectMany(p=>interests.Where(i=>RecommendationQuality.Matches(p,i))).Distinct().Count()*120;
                    score -= estimate.Array("legs")!.OfType<JsonObject>().Sum(x => x.Number("minutes")) * 0.35;
                    score -= estimate.Int("waitingMinutes")*0.1;
                    score += 100 * order.Count(x => mustVisit.Contains(x.Str("id")));
                    if(relaxed)score-=Math.Max(0,order.Length-2)*4;
                    options.Add((score, order,estimate.Array("legs")!.OfType<JsonObject>().Sum(x=>x.Number("minutes")),estimate.Int("waitingMinutes")));
                }
            }

            JsonObject? selected = null;
            // Compare bounded, verified alternatives instead of accepting the first feasible estimate.
            // Reserve timetable calls for later days; cache hits remain shared within this request.
            var limit=context.CompareTransit ? Math.Clamp((40-context.TransitQueries)/Math.Max(1,(profile.Int("days")-day)*(dailyLimit+1)),1,3):context.Walking is null ? 8:int.MaxValue;
            var best=double.NegativeInfinity;
            foreach (var option in options.OrderByDescending(x => x.Score).Take(limit))
            {
                var candidate = await ScheduleAsync(option.Order, daily, context, exact: true, ct);
                if(candidate is null)continue;
                var actual=candidate.Array("legs")!.OfType<JsonObject>().Sum(x=>x.Number("minutes"));
                var score=option.Score-(actual-option.Travel)*0.35-(candidate.Int("waitingMinutes")-option.Waiting)*0.1;
                if(score>best){selected=candidate;best=score;}
            }
            if (selected is null && required.Count == 0 && (used.Count > 0 || pool.Length == 0))
            {
                selected=await ScheduleAsync([],daily,context,exact:true,ct);
                if (selected is not null) {
                    selected["freeTime"]=true;
                    selected.Array("warnings")!.Add("自由安排：剩余候选景点在当天的预算、步数或营业时间约束下无法安排；当天未安排景点活动，可自行补充地点。");
                }
            }
            if (selected is null)
                throw new ServiceException($"第 {day + 1} 天没有满足预算、步数、营业时间和返程条件的路线。请增加时间/预算/步数，或减少必去地点。");
            if(profile.Str("dayStyle")=="fullDay" && string.CompareOrdinal(selected.Str("finishTime"),"15:00")<0) {
                selected.Array("warnings")!.Add("全天可用不代表必须排满：当前候选地点还需满足步数、预算、开放时间和跨天去重。余下时间可补附近地点、安排休息，或保留自由活动。");
            }
            selected["date"] = daily.Str("date");
            days.Add(selected);
            used.UnionWith(selected.Array("stops")!.OfType<JsonObject>().Select(x => x.Str("id")));
        }

        if (!mustVisit.IsSubsetOf(used)) throw new ServiceException("无法安排全部必去地点，请放宽约束。");
        RecommendationQuality.Assess(profile,days);
        return days;
    }

    private async Task<JsonObject?> ScheduleAsync(IReadOnlyList<JsonObject> order, JsonObject profile, PlanningContext context, bool exact, CancellationToken ct)
    {
        var clock = profile.Int("startMinute");
        var totalSteps = 0;
        var waitingMinutes=0;
        var totalCost = 0d;
        var stops = new JsonArray();
        var legs = new JsonArray();
        var warnings = new JsonArray();
        var previous = profile.Object("start")!;
        foreach (var point in order)
        {
            var leg = exact ? await routes.TimedLegAsync(previous,point,profile,clock,context,ct):context.Walking is not null ? await routes.LegAsync(previous, point, context, ct):RouteService.EstimateLeg(previous, point);
            if(leg.Bool("unreachable")){if(context.CompareTransit&&!exact)leg=RouteService.EstimateLeg(previous,point);else return null;}
            var steps = (int)Math.Ceiling(leg.Number("walkingMeters",leg.Number("meters")) / 0.75) + (int)Math.Ceiling(point.Number("visitMinutes") * (RecommendationQuality.IsMeal(point)?2:12));
            // 预留 10 分钟衔接及每站 15 分钟休息；步数包含场内参观和最后返程。
            // Account for transfer/rest buffers, internal walking, and the final return leg.
            var arrival = clock + (int)leg.Number("minutes") + 10;
            var windows = OpeningWindows(point, profile.Str("date"), out var verified);
            if(RecommendationQuality.IsMeal(point)) {
                if(RecommendationQuality.MealWindow(profile) is not {} meal)return null;
                windows=windows.Select(w=>(Open:Math.Max(w.Open,meal.Open),Close:Math.Min(w.Close,meal.Close))).ToArray();
            }
            var feasible = windows.Select(x => (Start: Math.Max(arrival, x.Open), x.Close))
                .Where(x => x.Start + point.Int("visitMinutes") <= x.Close).OrderBy(x => x.Start).ThenBy(x => x.Close).ToArray();
            if (feasible.Length == 0) return null;
            var start = feasible[0].Start;
            waitingMinutes+=Math.Max(0,start-arrival);
            clock = start + point.Int("visitMinutes") + 15;
            totalSteps += steps;
            totalCost += point.Number("cost");
            if (totalCost > profile.Number("budget") || (exact||!context.CompareTransit)&&totalSteps > profile.Int("maxSteps")) return null;
            var stop = CopyFields(point, "id", "name", "lat", "lng", "category", "indoor", "visitMinutes", "cost", "note", "activityType", "tags", "sourceURL", "source", "website", "costKnown", "indoorKnown", "openingHoursText");
            stop["arrival"] = TimeLabel(start);
            stop["departure"] = TimeLabel(start + point.Int("visitMinutes"));
            stop["hoursVerified"] = verified;
            stop["mapsURL"] = point.Str("mapsURL");
            stop["steps"] = steps;
            stops.Add(stop);
            legs.Add(leg);
            previous = point;
            if(point["costKnown"] is not null&&!point.Bool("costKnown"))warnings.Add(point.Str("name")+"：费用未知，预算仅含已知或预留金额，不代表免费。");
            if (!verified) warnings.Add(point.Str("name") + "：营业时间未核实");
        }
        var back = exact ? await routes.TimedLegAsync(previous,profile.Object("end")!,profile,clock,context,ct):context.Walking is not null ? await routes.LegAsync(previous, profile.Object("end")!, context, ct):RouteService.EstimateLeg(previous,profile.Object("end")!);
        if(back.Bool("unreachable")){if(context.CompareTransit&&!exact)back=RouteService.EstimateLeg(previous,profile.Object("end")!);else return null;}
        totalSteps += (int)Math.Ceiling(back.Number("walkingMeters",back.Number("meters")) / 0.75);
        clock += (int)back.Number("minutes") + 10;
        if (clock > profile.Int("endMinute") || (exact||!context.CompareTransit)&&totalSteps > profile.Int("maxSteps")) return null;
        legs.Add(back);
        return new JsonObject
        {
            ["planningStartMinute"]=profile.Int("startMinute"),["waitingMinutes"]=waitingMinutes,
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
