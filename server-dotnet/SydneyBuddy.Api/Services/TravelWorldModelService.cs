using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>Trip-revision-scoped checks over saved route observations. Never invents an optimized itinerary.</summary>
public sealed class TravelWorldModelService(WorldRepository worlds)
{
    public async Task<JsonObject> SimulateAsync(string tripId, string requestId, CancellationToken ct = default)
    {
        ValidateId(tripId); ValidateId(requestId);
        await using var transaction = await worlds.BeginAsync("world-trip:" + tripId, ct);
        var (trip, revision) = await worlds.GetTripAsync(tripId, ct);
        var world = await worlds.EnsureAsync(tripId, ct);
        var existing = await worlds.FindRunAsync(world.Id, requestId, ct);
        if (existing is not null) { var result = await worlds.GetRunAsync(existing.Id, ct); await transaction.CommitAsync(ct); return result; }
        var previous = await worlds.GetLatestForTripAsync(tripId, ct);
        var evaluation = Evaluate(trip); // Validate before recording any run.
        var run = await worlds.CreateRunAsync(world, requestId, revision, ct);
        await worlds.AppendEventAsync(run, "simulation_started", "simulation:" + requestId, "simulation_engine",
            new JsonObject { ["tripId"] = tripId, ["tripRevision"] = revision, ["source"] = "saved_route_observations" }, ct);
        foreach (var snapshot in evaluation.Array("snapshots")!.OfType<JsonObject>())
            await worlds.AppendSnapshotAsync(run, (JsonObject)snapshot.DeepClone(), ct);
        await worlds.AppendSnapshotAsync(run, new JsonObject {
            ["day"] = trip.Array("days")!.Count, ["eventType"] = "simulation_completed", ["time"] = "", ["location"] = "行程检查完成",
            ["spent"] = evaluation.Object("summary")!.Number("cost"), ["steps"] = evaluation.Object("summary")!.Int("walkingSteps"),
            ["transportMinutes"] = evaluation.Object("summary")!.Int("transportMinutes"), ["energyRisk"] = evaluation.Object("summary")!.Str("energyRisk"),
            ["weatherRisk"] = evaluation.Object("summary")!.Str("weatherRisk"), ["nextStop"] = "查看检查结果",
            ["summary"] = evaluation["summary"]!.DeepClone(), ["suggestions"] = evaluation["suggestions"]!.DeepClone()
        }, ct);
        await worlds.AppendEventAsync(run, "simulation_completed", "simulation_complete:" + requestId, "simulation_engine", new JsonObject { ["source"] = "saved_route_rules" }, ct);
        // Keep same-revision refusals across rechecks; changed itineraries must be evaluated afresh.
        if (previous.Object("run") is {} prior && prior.Int("tripRevision") == revision) {
            foreach (var choice in prior.Array("decisions")!.OfType<JsonObject>().Where(x=>x.Str("decision")=="rejected")) {
                var id = choice.Str("suggestionId");
                if (evaluation.Array("suggestions")!.OfType<JsonObject>().Any(x=>x.Str("id")==id))
                    await worlds.AppendEventAsync(run,"suggestion_decision","decision:"+run.Id+":"+id,"preserved_user_choice",
                        new JsonObject { ["suggestionId"]=id,["decision"]="rejected",["preserveUserChoice"]=true,["fromRunId"]=prior.Str("id") },ct);
            }
        }
        await worlds.CompleteRunAsync(run, ct);
        var rendered = await worlds.GetRunAsync(run.Id, ct);
        await transaction.CommitAsync(ct);
        return rendered;
    }

    public async Task<JsonObject> DecideAsync(string runId, string suggestionId, string decision, CancellationToken ct = default)
    {
        ValidateId(runId); ValidateId(suggestionId);
        if (decision is not ("accepted" or "rejected")) throw new ServiceException("决定必须是接受或拒绝。");
        await using var transaction = await worlds.BeginAsync("world-run:" + runId, ct);
        var rendered = await worlds.GetRunAsync(runId, ct);
        if (rendered.Bool("stale")) throw new ServiceException("行程已经改变，请重新检查后再调整。");
        if (!(rendered.Array("suggestions")?.OfType<JsonObject>().Any(x => x.Str("id") == suggestionId) ?? false)) throw new ServiceException("这条建议不属于本次检查。");
        var previous = rendered.Array("decisions")?.OfType<JsonObject>().FirstOrDefault(x => x.Str("suggestionId") == suggestionId);
        if (previous is not null) {
            if (previous.Str("decision") != decision) throw new ServiceException("这条建议已有决定；可以重新检查，或直接修改当天安排。");
            await transaction.CommitAsync(ct); return rendered;
        }
        var run = new WorldRunEntity { Id = runId, WorldId = rendered.Str("worldId"), RequestId = "", Kind = "", TripRevision = 0, Status = "", CreatedAt = "" };
        await worlds.AppendEventAsync(run, "suggestion_decision", "decision:" + runId + ":" + suggestionId, "user",
            new JsonObject { ["suggestionId"] = suggestionId, ["decision"] = decision, ["preserveUserChoice"] = decision == "rejected" }, ct);
        var result = await worlds.GetRunAsync(runId, ct); await transaction.CommitAsync(ct); return result;
    }

    public static JsonObject Evaluate(JsonObject trip)
    {
        var profile = trip.Object("profile") ?? throw new ServiceException("行程缺少旅行设置，无法检查。");
        var days = trip.Array("days")?.OfType<JsonObject>().ToArray() ?? [];
        if (days.Length is <1 or >15) throw new ServiceException("行程没有可检查的日期。");
        var snapshots = new JsonArray(); var findings = new JsonArray(); var daily = new JsonArray();
        var settings = trip.Object("schedule")?.Object("settings");
        var weather = trip.Object("weather");
        for (var d = 0; d < days.Length; d++) {
            var day = days[d];
            var scheduleDay = trip.Object("schedule")?.Array("days")?.OfType<JsonObject>().FirstOrDefault(x=>x.Str("date")==day.Str("date"));
            var limit = Math.Max(1, scheduleDay?.Int("maxSteps",settings?.Int("maxSteps",profile.Int("maxSteps",10000)) ?? profile.Int("maxSteps",10000)) ?? settings?.Int("maxSteps",profile.Int("maxSteps",10000)) ?? profile.Int("maxSteps",10000));
            var rawBudget = scheduleDay?.Number("dailyBudget",settings?.Number("dailyBudget",profile.Number("budget",double.NaN)) ?? profile.Number("budget",double.NaN)) ?? settings?.Number("dailyBudget",profile.Number("budget",double.NaN)) ?? profile.Number("budget",double.NaN);
            var budgetKnown = double.IsFinite(rawBudget) && rawBudget>=0;
            var budget = budgetKnown ? rawBudget:0;
            var stops = day.Array("stops")?.OfType<JsonObject>().ToArray() ?? [];
            var legs = day.Array("legs")?.OfType<JsonObject>().ToArray() ?? [];
            var steps = Math.Max(0, day.Int("estimatedSteps")); var cost = Math.Max(0, day.Number("estimatedCost"));
            var transport = (int)Math.Ceiling(legs.Sum(x => Math.Max(0, x.Number("minutes"))));
            var routeKnown = legs.Length == stops.Length + 1 && legs.All(x => !string.IsNullOrWhiteSpace(x.Str("source")) && !x.Str("source").Contains("estimate", StringComparison.OrdinalIgnoreCase) && !x.Str("source").Contains("demo", StringComparison.OrdinalIgnoreCase) && !new[]{"估算","估計","示例","演示"}.Any(word=>x.Str("source").Contains(word)) && !x.Bool("unreachable"));
            var forecast = weather?.Array("days")?.OfType<JsonObject>().FirstOrDefault(x => x.Str("date") == day.Str("date"));
            var weatherKnown = forecast?.Bool("available") == true;
            var rainy = weatherKnown && forecast!.Bool("rainy");
            var outdoor = stops.Where(x => !x.Bool("indoor") && x["indoorKnown"]?.ToString() != "false").Sum(x => Math.Max(0, x.Int("visitMinutes")));
            var costKnown = stops.All(x => x["costKnown"]?.ToString() != "false");
            var indoorKnown = stops.All(x => x["indoorKnown"]?.ToString() != "false");
            var energyRisk = Risk(steps, limit); var budgetRisk = !budgetKnown ? "unknown" : cost>budget ? "high" : costKnown ? Risk(cost, budget) : "unknown";
            var weatherRisk = !weatherKnown || !indoorKnown ? "unknown" : rainy && outdoor > 0 ? "high" : "low";
            var row = new JsonObject { ["day"] = d + 1, ["date"] = day.Str("date"), ["walkingSteps"] = steps, ["cost"] = cost,
                ["transportMinutes"] = transport, ["outdoorMinutes"] = outdoor, ["energyRisk"] = energyRisk, ["budgetRisk"] = budgetRisk,
                ["weatherRisk"] = weatherRisk, ["routeKnown"] = routeKnown, ["costKnown"] = costKnown, ["weatherKnown"] = weatherKnown };
            daily.Add(row);
            var start = scheduleDay?.Int("startMinute",day.Int("planningStartMinute",profile.Int("startMinute",600))) ?? day.Int("planningStartMinute",profile.Int("startMinute",600));
            snapshots.Add(State(d+1,"day_started",Time(start),day.Object("start")?.Str("name") ?? "出发点",0,0,0,"low","unknown",stops.FirstOrDefault()?.Str("name") ?? "返回点"));
            var spent = 0d; var walked = 0; var elapsed = 0;
            for (var i = 0; i < stops.Length; i++) {
                var stop = stops[i]; spent += Math.Max(0, stop.Number("cost"));
                // Saved per-stop steps reflect its inbound leg and activity. Do not spread daily steps evenly.
                walked += Math.Max(0, stop.Int("steps")); elapsed += (int)Math.Ceiling(Math.Max(0, legs.ElementAtOrDefault(i)?.Number("minutes") ?? 0));
                snapshots.Add(State(d+1,"arrived",stop.Str("arrival",Time(start)),stop.Str("name"),spent,walked,elapsed,Risk(walked,limit),
                    !weatherKnown || stop["indoorKnown"]?.ToString()=="false" ? "unknown" : rainy && !stop.Bool("indoor") ? "high":"low",
                    stops.ElementAtOrDefault(i+1)?.Str("name") ?? day.Object("end")?.Str("name") ?? "返回点"));
            }
            snapshots.Add(State(d+1,"returned",day.Str("finishTime"),day.Object("end")?.Str("name") ?? "返回点",cost,steps,transport,energyRisk,"unknown","当天检查完成"));
            void Add(string type, string title, string detail, string action) => findings.Add(new JsonObject { ["id"] = type+"-"+d, ["dayIndex"] = d, ["title"] = title, ["detail"] = detail, ["action"] = action, ["status"] = "pending" });
            if (energyRisk is "high" or "medium") Add("energy","这一天可能有点累",$"第 {d+1} 天约 {steps:N0} 步，你的每日上限是 {limit:N0} 步。可以缩减可选站点或留出休息。","adjust");
            if (budgetRisk == "high") Add("budget","这天的小钱包有点紧",$"第 {d+1} 天每人活动估算 A${cost:0.##}，每日预算 A${budget:0.##}。未包含未确认费用。","adjust");
            if (weatherRisk == "high") Add("weather","带你找个躲雨的地方？",$"第 {d+1} 天已保存的预报有雨，约有 {outdoor} 分钟户外活动。天气可能变化，请先核实。","replace");
            if (transport >= 120) Add("transport","这天在路上的时间比较多",$"第 {d+1} 天交通合计约 {transport} 分钟。是否绕路还需结合路线比较，不能只凭总时长判断。","adjust");
            if (!routeKnown || !weatherKnown || !costKnown || !indoorKnown) Add("verify","还有一些资料待确认",$"第 {d+1} 天："+string.Join("、",new[]{!routeKnown?"部分交通为估算":null,!weatherKnown?"当天天气未知":null,!costKnown?"部分费用未知":null,!indoorKnown?"部分室内外信息未知":null}.Where(x=>x is not null))+"。未知不代表没有风险。","review");
        }
        string Overall(string key) {var values=daily.OfType<JsonObject>().Select(x=>x.Str(key)).ToArray();return values.Contains("high")?"high":values.Contains("unknown")?"unknown":values.Contains("medium")?"medium":"low";}
        return new JsonObject { ["summary"] = new JsonObject {
            ["walkingSteps"] = daily.OfType<JsonObject>().Sum(x=>x.Int("walkingSteps")), ["cost"] = daily.OfType<JsonObject>().Sum(x=>x.Number("cost")),
            ["transportMinutes"] = daily.OfType<JsonObject>().Sum(x=>x.Int("transportMinutes")), ["outdoorMinutes"] = daily.OfType<JsonObject>().Sum(x=>x.Int("outdoorMinutes")),
            ["energyRisk"] = Overall("energyRisk"), ["budgetRisk"] = Overall("budgetRisk"), ["weatherRisk"] = Overall("weatherRisk"),
            ["days"] = daily, ["source"] = "已保存的路线与预报；步数和费用为估算，不是实时位置或实际支出", ["weatherFetchedAt"] = weather?.Str("fetchedAt") ?? ""
        }, ["suggestions"] = findings, ["snapshots"] = snapshots };
    }
    private static JsonObject State(int day,string type,string time,string location,double cost,int steps,int transport,string energy,string weather,string next) => new() {
        ["day"]=day,["eventType"]=type,["time"]=time,["location"]=location,["spent"]=Math.Round(cost,2),["steps"]=steps,["transportMinutes"]=transport,["energyRisk"]=energy,["weatherRisk"]=weather,["nextStop"]=next };
    private static string Risk(double value,double limit) => (value/Math.Max(1,limit)) switch { >1=>"high", >.8=>"medium", _=>"low" };
    private static string Time(int minute) => $"{Math.Clamp(minute,0,1439)/60:00}:{Math.Clamp(minute,0,1439)%60:00}";
    private static void ValidateId(string value) { if(string.IsNullOrWhiteSpace(value)||value.Length>120||value.Any(x=>!(char.IsLetterOrDigit(x)||x is '-' or '_')))throw new ServiceException("检查编号不合法。"); }
}
