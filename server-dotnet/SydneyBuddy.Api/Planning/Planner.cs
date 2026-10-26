using System.Globalization;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 规划用例编排：校验输入、收集证据、获得推荐、检查路线，再返回未保存草案。
/// Orchestrates validation, evidence, recommendations and scheduling; returns drafts without persistence.
/// </summary>
public sealed class Planner(PlaceService places, RecommendationService recommendations, RouteScheduler scheduler,
    WeatherService weather, SecretStore secrets,TravelDataContext? travel=null,PublicTravelWeather? publicWeather=null,PlanningAgent? agent=null)
{
    public IReadOnlyList<JsonObject> Catalog => places.Catalog;

    /// <summary>输入行程日期/时间，输出天气状态；Input: travel profile. Output: explicit forecast availability.</summary>
    public async Task<JsonObject> WeatherAsync(JsonObject raw, CancellationToken ct = default)
    {
        var context = new PlanningContext("live", secrets.Read());
        return await WeatherForProfileAsync(travel?.Explicit==true ? PlanValidator.ValidateForecast(raw):PlanValidator.Validate(raw, Catalog), context, ct);
    }

    /// <summary>
    /// 输入客户端约束与模式，返回兼容原客户端的未保存行程。
    /// Takes constraints and mode; returns an unsaved itinerary with the original mobile JSON contract.
    /// </summary>
    public async Task<JsonObject> PlanAsync(JsonObject raw, string mode, CancellationToken ct = default,bool shortlist=false)
    {
        if (mode is not ("demo" or "assisted" or "live")) throw new ServiceException("Invalid mode");
        var context = new PlanningContext(mode, secrets.Read());
        var imported=raw.Array("planningCandidates");
        var planningCatalog=PlanningCandidates.Merge(Catalog,imported);
        var evidence=raw.Array("walkingRoutes");
        var candidateDays=raw.Array("walkingDayCandidateIds");
        raw=(JsonObject)raw.DeepClone();raw.Remove("walkingRoutes");raw.Remove("walkingDayCandidateIds");raw.Remove("planningCandidates");
        if(mode=="live" && evidence is not null)throw new ServiceException("Trips using Google data cannot include Apple walking data.");
        if(evidence is not null)context.Walking=new WalkingEvidence(evidence);
        context.CompareTransit=evidence is not null && raw.Str("transportPreference","walk")!="walk";
        var profile = PlanValidator.Validate(raw, planningCatalog);
        var candidates = TravelConstraints.Apply(profile, imported is null ? await places.GetCandidatesAsync(context, ct):planningCatalog.Where(p=>PoiService.IsImported(p)||profile.Array("mustVisit")!.Any(id=>id!.GetValue<string>()==p.Str("id"))||profile.Str("mustDo").Contains(p.Str("name"))).ToArray());
        var uncertainEventCount=planningCatalog.Count(SeasonalEventGuard.NeedsVerifiedDates);
        if(uncertainEventCount>0)context.Record("Check limited-time events",$"{uncertainEventCount} candidates may be seasonal or limited-time events. Map search results do not verify availability for your dates, so they were excluded from automatic recommendations. This does not mean they have closed or expired.","Conservative candidate check: event dates unverified");
        if(candidateDays is not null) {
            var known=planningCatalog.Select(p=>p.Str("id")).ToHashSet(StringComparer.Ordinal);
            if(context.Walking is null || candidateDays.Count!=profile.Int("days"))throw new ServiceException("Daily walking candidates are incomplete. Please generate them again.");
            var groups=new List<HashSet<string>>();
            foreach(var value in candidateDays) {
                if(value is not JsonArray ids || ids.Count>10)throw new ServiceException("Invalid daily walking candidates.");
                var group=new HashSet<string>(StringComparer.Ordinal);
                foreach(var node in ids) {
                    if(node is not JsonValue v || !v.TryGetValue<string>(out var id)||!known.Contains(id)||!group.Add(id))throw new ServiceException("Daily walking candidates contain an invalid place.");
                }
                groups.Add(group);
            }
            if(!profile.Array("mustVisit")!.All(id=>groups.Any(g=>g.Contains(id!.GetValue<string>()))))throw new ServiceException("The walking candidates are missing a must-see place.");
            context.DayCandidateIds=groups;
            context.Record("Check routes by area","Select each day's main activities and nearby alternatives, then compare their order using actual walking times. This does not search every route in the city.","Local recommendation rules + Apple Maps");
        }

        if(raw.Array("walkingCandidateIds") is {} selected) {
            var ids=selected.Select(x=>x?.GetValue<string>()??"").ToHashSet();
            if(context.Walking is null || ids.Count==0 || ids.Any(id=>!planningCatalog.Any(p=>p.Str("id")==id)) || profile.Array("mustVisit")!.Any(x=>!ids.Contains(x!.GetValue<string>())))throw new ServiceException("Candidate places are incomplete. Please search for walking directions again.");
            candidates=candidates.Where(p=>ids.Contains(p.Str("id"))).ToArray();
            context.Record("Shortlist walking candidates",$"Compare directional travel times for {candidates.Count} candidate places while keeping your must-see places.","Apple Maps / MapKit client directions");
        }
        profile.Remove("walkingCandidateIds");
        profile.Remove("useAgent");
        var forecast = await WeatherForProfileAsync(profile, context, ct);
        var choice = raw.Bool("useAgent") ? await (agent??throw new ServiceException("The travel agent has not started.")).RankAsync(profile,candidates,ct):await recommendations.SelectAsync(profile, candidates, forecast, context, ct);
        if(raw.Bool("useAgent"))context.Record("Koko Agent recommendations",choice.Str("explanation"),"NVIDIA NIM / "+secrets.Read().GetValueOrDefault("NVIDIA_MODEL",SecretStore.DefaultModel));
        var ranks = choice.Array("preferredIds")!.Select((id, index) => (id: id!.GetValue<string>(), index))
            .ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);
        if (!shortlist && TravelAdjustmentIntent.Lighter(profile.Str("request")))
            profile["maxSteps"] = Math.Min(profile.Int("maxSteps"), 7000);
        var days = await scheduler.ScheduleDaysAsync(profile, candidates, forecast, ranks, context, ct);
        context.Record("Compare transport options",context.CompareTransit ? "Compare TfNSW services, waiting, walking connections and transfers on shortlisted routes. Fares are unknown.":"Walking-only selected, or a legacy request is being supported.","Transport planner");
        var draft=BuildDraft(profile, days, forecast, context);
        var transportEvidence=new JsonArray();
        foreach(var day in days.OfType<JsonObject>()) {
            var stops=day.Array("stops")!.OfType<JsonObject>().ToArray();var previous=day.Object("start")!;var clock=day.Int("planningStartMinute");var index=0;
            foreach(var leg in day.Array("legs")!.OfType<JsonObject>()) {
                var next=index<stops.Length?stops[index]:day.Object("end")!;
                if(leg.Str("mode")=="transit")transportEvidence.Add(new JsonObject{["from"]=previous.DeepClone(),["to"]=next.DeepClone(),["date"]=day.Str("date"),["minute"]=clock,["leg"]=leg.DeepClone()});
                if(index<stops.Length){var t=stops[index].Str("departure").Split(':');clock=int.Parse(t[0])*60+int.Parse(t[1])+15;}
                previous=next;index++;
            }
        }
        if(context.CompareTransit)draft["transportRoutes"]=transportEvidence;
        if(imported is not null){draft["planningCandidates"]=new JsonArray(planningCatalog.Where(PoiService.IsImported).Select(p=>p.DeepClone()).ToArray());draft.Array("warnings")!.Add("Places come from this search. Unknown admission fees, opening hours and meal prices remain unverified, so the budget may be incomplete.");}
        return draft;
    }

    // This shortlist is not a final itinerary: the client must verify its directed road legs.
    public async Task<JsonObject> PrepareWalkingAsync(JsonObject raw,CancellationToken ct=default)
    {
        var input=(JsonObject)raw.DeepClone();input.Remove("mode");input.Remove("walkingRoutes");input.Remove("walkingDayCandidateIds");input.Remove("walkingCandidateIds");
        // A far must-see must reach the transit comparison instead of being rejected by a walking-only prefilter.
        if(input.Str("transportPreference","walk")!="walk"){input["maxSteps"]=60000;input["endMinute"]=1439;}
        var seed=await PlanAsync(input,"demo",ct,shortlist:true);
        var profile=seed.Object("profile")!;var eligible=TravelConstraints.Apply((JsonObject)profile.DeepClone(),PlanningCandidates.Merge(Catalog,raw.Array("planningCandidates")));
        var days=seed.Array("days")!.OfType<JsonObject>().ToArray();
        if(raw.Array("planningCandidates") is not null)eligible=eligible.Where(PoiService.IsImported).ToArray();
        var assigned=days.SelectMany(d=>d.Array("stops")!.OfType<JsonObject>()).Select(p=>p.Str("id")).ToHashSet();
        var groups=new JsonArray();var interests=profile.Array("interests")!.Select(i=>i!.GetValue<string>()).ToArray();
        foreach(var day in days) {
            var picks=day.Array("stops")!.OfType<JsonObject>().ToArray();var ids=picks.Select(p=>p.Str("id")).ToList();
            if(picks.Length>0) {
                var backup=eligible.Where(p=>!assigned.Contains(p.Str("id")))
                    .OrderByDescending(p=>RecommendationQuality.InterestScore(p,interests)-picks.Min(s=>RouteService.Distance(s,p))/100)
                    .ThenBy(p=>p.Str("id"),StringComparer.Ordinal).FirstOrDefault();
                if(backup is not null)ids.Add(backup.Str("id"));
            }
            groups.Add(new JsonArray(ids.Select(id=>JsonValue.Create(id)).ToArray()));
        }
        return new JsonObject{["candidates"]=seed["planningCandidates"]?.DeepClone(),["candidateDays"]=groups,["source"]=raw.Bool("useAgent")?"Koko Agent shortlisted search results; routes and timetables still need checking":"Local shortlist by theme and area; routes still need checking"};
    }

    private static JsonObject BuildDraft(JsonObject profile, JsonArray days, JsonObject weather, PlanningContext context)
    {
        context.Record("Validate the full route independently", "Check walking, visits, 10-minute connections, 15-minute breaks, the return journey, estimated costs and opening hours for each leg. Unverified items are listed explicitly.", "Deterministic rules engine");
        var warnings = new JsonArray
        {
            "Costs, steps within attractions and visit durations are estimates. Hotels, transport fares, some meals and special exhibition tickets are excluded.",
            context.CompareTransit?"Walking and public transport were compared. Accessibility, hills, queues and temporary road closures have not been assessed.":"Walking mode. Accessibility, hills, queues and temporary road closures have not been assessed."
        };
        if (profile.Array("mustVisit")!.Count>0 && !string.IsNullOrEmpty(profile.Str("mustDo")))warnings.Add("Must-see or must-do: "+profile.Str("mustDo")+". Checked as a required stop and included in the route.");
        if (!string.IsNullOrEmpty(profile.Str("avoid")) && profile.Str("avoid") is not ("无" or "未确定" or "暂不确定" or "无特别要求"))warnings.Add("Avoid or travel needs: "+profile.Str("avoid")+". Recognized places and categories were filtered. Accessibility and temporary closures still need checking.");
        if (!string.IsNullOrEmpty(profile.Str("origin")) && profile.Str("origin") != profile.Object("start")!.Str("name"))warnings.Add("Starting point note: "+profile.Str("origin")+"; the current starting point is "+profile.Object("start")!.Str("name")+". Please check the location.");
        if (!string.IsNullOrEmpty(profile.Str("queueTolerance")))warnings.Add("Your queue preference is recorded, but reliable live queue data is unavailable, so waiting times cannot be guaranteed.");
        if (days.OfType<JsonObject>().Any(d=>d.Bool("freeTime")))
            warnings.Add("Some days have free time because the available places do not support unique activities every day within your budget, step limit and opening hours. Review the schedule and add places if needed.");
        if (weather.Bool("rainy") && days.OfType<JsonObject>()
                .SelectMany(x => x.Array("stops")!.OfType<JsonObject>()).Any(x => !x.Bool("indoor")))
            warnings.Add("Your trip includes outdoor activities. Check the weather before departure and have indoor alternatives ready.");
        if(context.CompareTransit)warnings.Add("Walking and NSW public transport were compared. Timetables reflect this search and need refreshing before departure. Fares are unavailable and excluded from the budget.");
        if(context.Walking is not null)warnings.Insert(0,JsonValue.Create("Walking was verified using Apple directions. Place costs and unverified opening hours remain estimates."));
        else if (context.Mode == "demo") warnings.Insert(0, JsonValue.Create("Demo mode: Google and Nemotron were not called. This route is not navigation guidance."));
        else if (context.Mode == "assisted") warnings.Insert(0, JsonValue.Create("Nemotron was called. Places, opening hours and routes remain sample estimates. See the displayed weather source; Google weather was not queried."));
        var allStops=days.OfType<JsonObject>().SelectMany(d=>d.Array("stops")!.OfType<JsonObject>()).ToArray();
        var unmet=profile.Array("interests")!.Select(i=>i!.GetValue<string>()).Where(i=>!allStops.Any(p=>RecommendationQuality.Matches(p,i))).ToArray();
        foreach(var interest in unmet)warnings.Add("Unmet preference: "+RecommendationQuality.InterestLabel(interest)+". Available places or travel limits do not satisfy it; other activities have not been presented as a substitute.");
        foreach(var day in days.OfType<JsonObject>())foreach(var w in day.Array("warnings")?.OfType<JsonValue>()??[]) {
            var text=w.GetValue<string>();if(text.Contains("Core interests are not covered")||text.Contains("No specific restaurant is scheduled")||text.Contains("half-day activities"))warnings.Add(day.Str("date")+": "+text);
        }
        var partial=unmet.Length>0||days.OfType<JsonObject>().Any(d=>d.Bool("freeTime")||d.Str("recommendationStatus")=="partial");
        if(allStops.Any(RecommendationQuality.IsMeal))warnings.Add("Specific restaurants are included. Meal costs are per-person estimates included in the daily budget, not menu quotes, bookings or availability guarantees. Check opening hours before departure.");
        var explanation = $"Planned {allStops.Length} activities, including {allStops.Count(RecommendationQuality.IsMeal)} specific meals, across {days.Count} days. Daily activity budget: A${profile.Number("budget").ToString("G", CultureInfo.InvariantCulture)}; step limit: {profile.Int("maxSteps")}. Transport, visits, breaks and the return to {profile.Object("end")!.Str("name")} are included.";
        if (weather.Bool("rainy")) explanation += " Rain is forecast, so indoor activities are prioritized and optional outdoor visits shortened. Check the weather before departure.";
        if(days.OfType<JsonObject>().Any(d=>d.Array("warnings")!.Any(w=>w!.GetValue<string>().Contains("half-day activities"))))explanation+=" Some days include only half-day activities and meals, with the afternoon left free rather than treated as fully planned.";
        if(partial)explanation="This draft needs more work; some requirements are not yet met. "+explanation;
        return new JsonObject
        {
            ["recommendationStatus"]=partial?"partial":"ready",["title"] = "A Little Sydney Adventure", ["mode"] = context.Mode, ["profile"] = profile, ["days"] = days,
            ["dataSource"] = weather.Str("dataSource","legacy"), ["weather"] = weather, ["explanation"] = explanation, ["warnings"] = warnings,
            ["trace"] = context.Trace.DeepClone(), ["createdAt"] = context.Now
        };
    }

    private async Task<JsonObject> WeatherForProfileAsync(JsonObject profile, PlanningContext context, CancellationToken ct)
    {
        var value = travel?.Explicit==true && publicWeather is not null ? await publicWeather.GetAsync(profile,ct) : context.Mode == "live"
            ? await weather.GetAsync(profile, context.Config.GetValueOrDefault("GOOGLE_SERVER_KEY"), ct)
            : new JsonObject { ["summary"] = "Sunny (demo)", ["rainy"] = false, ["source"] = "Demo assumption" };
        if (profile.Str("weatherOverride") == "rain")
            value = new JsonObject { ["summary"] = "Rain scenario (selected by you)", ["rainy"] = true, ["source"] = "User-selected scenario, not live weather" };
        context.Record("Check weather", value.Str("summary"), value.Str("source"));
        return value;
    }

    // 兼容已有领域测试的入口；实现职责分别属于推荐、调度和路段服务。
    // Preserve existing test entry points while delegating to focused owners.
    public static JsonObject ParseModelJson(string content) => RecommendationService.ParseModelJson(content);
    public static IReadOnlyList<(int Open, int Close)> OpeningWindows(JsonObject place, string date, out bool verified) =>
        RouteScheduler.OpeningWindows(place, date, out verified);
    public static double Distance(JsonObject a, JsonObject b) => RouteService.Distance(a, b);
}
