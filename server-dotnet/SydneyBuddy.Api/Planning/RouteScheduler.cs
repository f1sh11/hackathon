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
                (!rainy || profile.Str("weatherTolerance")!="avoidRain" || x.Bool("indoor")&&(x["indoorKnown"] is null||x.Bool("indoorKnown")))).ToArray();
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
            var relaxed=DailyPacing.Low(daily) || profile.Str("rhythm")=="轻松随意";
            // Stop count follows feasible time, meals and walking load, never an energy-based four-stop rule.
            // Thirty is the shared serialized activity payload bound (including logistics), not a target.
            var taskCount=RouteLogistics.Tasks(daily,daily.Str("date"),true).Length+RouteLogistics.Tasks(daily,daily.Str("date"),false).Length;
            var dailyLimit=Math.Min(pool.Length,30-taskCount);
            var fairShare=Math.Max(1,(int)Math.Ceiling((double)available.Count(p=>!RecommendationQuality.IsMeal(p))/(profile.Int("days")-day)));
            var options = new List<(double Score, JsonObject[] Order, double Travel, int Waiting,double Walking)>();

            // A bounded beam prevents factorial growth; energy changes walking limits, not stop counts.
            var beam=new List<JsonObject[]>{Array.Empty<JsonObject>()};
            for (var length = 1; length <= dailyLimit; length++)
            {
                var level=new List<(double Score,JsonObject[] Order)>();
                var orders=beam.SelectMany(previous=>pool.Where(p=>previous.All(q=>q.Str("id")!=p.Str("id"))).Select(p=>previous.Append(p).ToArray()));
                foreach (var order in orders)
                {
                    ct.ThrowIfCancellationRequested();
                    var mealCount=order.Count(RecommendationQuality.IsMeal);
                    if(mealCount>MealTiming.Slots(daily).Length)continue;
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
                    score-=estimate.Array("legs")!.OfType<JsonObject>().Sum(l=>l.Number("walkingMeters",l.Number("meters")))*(DailyPacing.Low(daily)?.035:.02);
                    score-=estimate.Number("mealTimingPenalty");
                    score-=Math.Max(0,estimate.Int("estimatedSteps")-DailyPacing.ReferenceSteps(daily))*.04;
                    if(profile.Int("days")-day>1)score-=Math.Max(0,order.Count(p=>!RecommendationQuality.IsMeal(p))-fairShare)*12;
                    score+=DailyPacing.Coverage(estimate,daily);
                    level.Add((score,order));
                    if(required.IsSubsetOf(order.Select(x=>x.Str("id"))))options.Add((score, order,estimate.Array("legs")!.OfType<JsonObject>().Sum(x=>x.Number("minutes")),estimate.Int("waitingMinutes"),estimate.Array("legs")!.OfType<JsonObject>().Sum(l=>l.Number("walkingMeters",l.Number("meters")))));
                }
                {
                    beam=level.OrderByDescending(x=>x.Score).Take(24).Select(x=>x.Order).ToList();
                    if(beam.Count==0)break;
                }
            }

            JsonObject? selected = null;
            // Compare bounded, verified alternatives instead of accepting the first feasible estimate.
            // Reserve timetable calls for later days; cache hits remain shared within this request.
            var limit=context.CompareTransit ? Math.Clamp((40-context.TransitQueries)/Math.Max(1,(profile.Int("days")-day)*(dailyLimit+1)),1,3):context.Walking is null ? 8:16;
            var best=double.NegativeInfinity;
            var verify=options.OrderByDescending(x=>x.Score).Take(limit);
            if(!context.CompareTransit)verify=verify.Concat(interests.SelectMany(i=>options.Where(o=>o.Order.Any(p=>RecommendationQuality.Matches(p,i))).OrderBy(o=>o.Travel).ThenByDescending(o=>o.Score).Take(2)))
                .DistinctBy(o=>string.Join("|",o.Order.Select(p=>p.Str("id"))));
            foreach (var option in verify)
            {
                var candidate = await ScheduleAsync(option.Order, daily, context, exact: true, ct);
                if(candidate is null)continue;
                var actual=candidate.Array("legs")!.OfType<JsonObject>().Sum(x=>x.Number("minutes"));
                var walking=candidate.Array("legs")!.OfType<JsonObject>().Sum(l=>l.Number("walkingMeters",l.Number("meters")));
                var score=option.Score-(actual-option.Travel)*.35-(candidate.Int("waitingMinutes")-option.Waiting)*.1-(walking-option.Walking)*(DailyPacing.Low(daily)?.035:.02);
                var sameActivities=selected is not null&&candidate.Array("stops")!.OfType<JsonObject>().Select(p=>p.Str("id")).Order().SequenceEqual(selected.Array("stops")!.OfType<JsonObject>().Select(p=>p.Str("id")).Order());
                var selectedWalking=selected?.Array("legs")?.OfType<JsonObject>().Sum(l=>l.Number("walkingMeters",l.Number("meters")))??double.PositiveInfinity;
                candidate["routeOptimization"]=new JsonObject{["objective"]="less_walking_then_smoother_route",["walkingMeters"]=walking,["transportMinutes"]=actual,["scope"]="bounded feasible shortlist; not a global optimum proof"};
                if(sameActivities ? walking<selectedWalking-1||Math.Abs(walking-selectedWalking)<=1&&score>best : score>best){selected=candidate;best=score;}
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
            MealTiming.Annotate(selected,daily);
            DailyPacing.Annotate(selected,daily);
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
        var mealWindows=new HashSet<string>();
        var taskPrefix=RouteLogistics.Tasks(profile,profile.Str("date"),true);
        var taskSuffix=RouteLogistics.Tasks(profile,profile.Str("date"),false);
        var sequence=taskPrefix.Concat(order).Concat(taskSuffix);
        var mealPenalty=0d;
        foreach (var point in sequence)
        {
            var leg = exact ? await routes.TimedLegAsync(previous,point,profile,clock,context,ct):context.Walking is not null ? await routes.LegAsync(previous, point, context, ct):RouteService.EstimateLeg(previous, point);
            if(leg.Bool("unreachable")){if(context.CompareTransit&&!exact)leg=RouteService.EstimateLeg(previous,point);else return null;}
            var routeSteps=WalkingLoad.DistanceSteps(leg.Number("walkingMeters",leg.Number("meters")),profile);
            var activitySteps=WalkingLoad.VisitSteps(point,point.Int("visitMinutes"),profile,point.Str("activityType"));
            var steps=routeSteps+activitySteps;
            // 预留 10 分钟衔接及每站 15 分钟休息；步数包含场内参观和最后返程。
            // Account for transfer/rest buffers, internal walking, and the final return leg.
            var arrival = clock + (int)leg.Number("minutes") + 10;
            var windows = OpeningWindows(point, profile.Str("date"), out var verified);
            if(RecommendationQuality.IsMeal(point)) {
                windows=windows.SelectMany(w=>MealTiming.Slots(profile).Where(m=>!mealWindows.Contains(m.Name)).Select(m=>(Open:Math.Max(w.Open,m.Open),Close:Math.Min(w.Close,m.Close)))).ToArray();
            }
            var feasible = windows.Select(x => (Start: Math.Max(arrival, x.Open), x.Close))
                .Where(x => x.Start + point.Int("visitMinutes") <= x.Close).OrderBy(x => x.Start).ThenBy(x => x.Close).ToArray();
            if (feasible.Length == 0) return null;
            var start = feasible[0].Start;
            if(point.Str("activityType")=="bag-drop"&&profile.Int("startMinute")<720&&start>=720)return null;
            if(RecommendationQuality.IsMeal(point)){
                var slot=MealTiming.Slots(profile).First(m=>start>=m.Open&&start+point.Int("visitMinutes")<=m.Close&&!mealWindows.Contains(m.Name));
                mealPenalty+=Math.Abs(start-slot.Target)*.35;mealWindows.Add(slot.Name);
            }
            waitingMinutes+=Math.Max(0,start-arrival);
            clock = start + point.Int("visitMinutes") + (RouteLogistics.IsTask(point)?0:15);
            totalSteps += steps;
            totalCost += point.Number("cost");
            if (totalCost > profile.Number("budget") || (exact||!context.CompareTransit)&&totalSteps > profile.Int("maxSteps")) return null;
            var stop = CopyFields(point, "id", "name", "lat", "lng", "category", "indoor", "visitMinutes", "cost", "note", "activityType", "tags", "sourceURL", "source", "website", "costKnown", "indoorKnown", "openingHoursText", "storageConfirmed", "openMinute", "closeMinute");
            stop["arrival"] = TimeLabel(start);
            stop["departure"] = TimeLabel(start + point.Int("visitMinutes"));
            stop["hoursVerified"] = RouteLogistics.IsTask(point)?point.Bool("hoursVerified"):verified;
            stop["mapsURL"] = point.Str("mapsURL");
            stop["steps"] = steps;
            stop["routeSteps"]=routeSteps;stop["activitySteps"]=activitySteps;
            stop["activityWalkingMinutes"]=WalkingLoad.VisitMinutes(point,point.Int("visitMinutes"));
            stop["continuousWalkingSteps"]=(int)Math.Ceiling(point.Int("visitMinutes")*WalkingLoad.Cadence(profile));
            stop["stepEstimateVersion"]=WalkingLoad.Version;
            stops.Add(stop);
            legs.Add(leg);
            previous = point;
            if(point["costKnown"] is not null&&!point.Bool("costKnown"))warnings.Add(point.Str("name")+"：费用未知，预算仅含已知或预留金额，不代表免费。");
            if(RouteLogistics.IsTask(point)&&!point.Bool("storageConfirmed"))warnings.Add("行李寄放服務尚未確認：飯店或區域地點不等於可寄放，請核實服務時間、費用及容量；取件已列入返程前的草稿任務。");
            if (!verified) warnings.Add(point.Str("name") + "：营业时间未核实");
        }
        var back = exact ? await routes.TimedLegAsync(previous,profile.Object("end")!,profile,clock,context,ct):context.Walking is not null ? await routes.LegAsync(previous, profile.Object("end")!, context, ct):RouteService.EstimateLeg(previous,profile.Object("end")!);
        if(back.Bool("unreachable")){if(context.CompareTransit&&!exact)back=RouteService.EstimateLeg(previous,profile.Object("end")!);else return null;}
        totalSteps += WalkingLoad.DistanceSteps(back.Number("walkingMeters",back.Number("meters")),profile);
        clock += (int)back.Number("minutes") + 10;
        if (clock > profile.Int("endMinute") || (exact||!context.CompareTransit)&&totalSteps > profile.Int("maxSteps")) return null;
        legs.Add(back);
        return new JsonObject
        {
            ["planningStartMinute"]=profile.Int("startMinute"),["waitingMinutes"]=waitingMinutes,["mealTimingPenalty"]=mealPenalty,
            ["stops"] = stops, ["legs"] = legs, ["estimatedCost"] = totalCost, ["estimatedSteps"] = totalSteps,
            ["finishTime"] = TimeLabel(clock), ["warnings"] = warnings,
            ["stepEstimate"]=exact?WalkingLoad.Breakdown(profile,stops,legs,totalSteps):null,
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
