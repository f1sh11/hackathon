using System.Globalization;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 规划用例编排：校验输入、收集证据、获得推荐、检查路线，再返回未保存草案。
/// Orchestrates validation, evidence, recommendations and scheduling; returns drafts without persistence.
/// </summary>
public sealed class Planner(PlaceService places, RecommendationService recommendations, RouteScheduler scheduler,
    WeatherService weather, SecretStore secrets,TravelDataContext? travel=null,PublicTravelWeather? publicWeather=null,PlanningAgent? agent=null,PlanningInventory? inventory=null)
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
        if (mode is not ("demo" or "assisted" or "live")) throw new ServiceException("模式不合法");
        var context = new PlanningContext(mode, secrets.Read());
        var imported=raw.Array("planningCandidates");
        var autoInventory=false;
        var useAgent=raw.Bool("useAgent") || agent?.UsesSkill==true;
        context.MeterlessPlanning=useAgent&&agent?.UsesSkill==true;
        JsonObject? lists=null;
        if(useAgent&&agent?.UsesSkill==true&&inventory is not null) {
            lists=inventory.Read(raw,ct);
            if(imported is null&&lists.Array("activities")?.Count>0){
                autoInventory=true;
                var catalogIds=Catalog.Select(p=>p.Str("id")).ToHashSet();
                imported=new JsonArray(lists.Array("activities")!.OfType<JsonObject>().Where(p=>!catalogIds.Contains(p.Str("id"))).Select(p=>p.DeepClone()).ToArray());
            }
        }
        // Lodging is agent context and an unbooked suggestion, never an attraction or implicit new origin.
        var activityInput=imported is null?null:new JsonArray(imported.OfType<JsonObject>().Where(p=>!PlanningInventory.IsAccommodation(p)).Select(p=>p.DeepClone()).ToArray());
        if(imported is not null&&imported.Any(p=>p is not JsonObject))throw new ServiceException("規劃候選格式無效。");
        var planningCatalog=PlanningCandidates.Merge(Catalog.Where(p=>!PlanningInventory.IsAccommodation(p)).ToArray(),activityInput);
        var evidence=raw.Array("walkingRoutes");
        var candidateDays=raw.Array("walkingDayCandidateIds");
        raw=(JsonObject)raw.DeepClone();raw.Remove("walkingRoutes");raw.Remove("walkingDayCandidateIds");raw.Remove("planningCandidates");
        if(mode=="live" && evidence is not null)throw new ServiceException("Google 来源行程不能混用 Apple 步行数据。");
        if(evidence is not null)context.Walking=new WalkingEvidence(evidence);
        context.CompareTransit=evidence is not null && raw.Str("transportPreference","walk")!="walk";
        var profile = PlanValidator.Validate(raw, planningCatalog);
        if(DailyPacing.Tired(profile)){
            profile["energyFeedback"]="tired";
            if(!shortlist)profile["maxSteps"]=Math.Min(profile.Int("maxSteps"),8000);
        }else if(DailyPacing.Energized(profile))profile["energyFeedback"]="energized";
        var candidates = TravelConstraints.Apply(profile, imported is null ? await places.GetCandidatesAsync(context, ct):planningCatalog.Where(p=>autoInventory||PoiService.IsImported(p)||profile.Array("mustVisit")!.Any(id=>id!.GetValue<string>()==p.Str("id"))||profile.Str("mustDo").Contains(p.Str("name"))).ToArray());
        var locationConflictCount=planningCatalog.Count(PlanningLocationGuard.HasKnownConflict);
        if(locationConflictCount>0)context.Record("核对地点位置",$"发现 {locationConflictCount} 个已确认的来源地点身份与坐标冲突，已从自动规划中排除，未更改来源数据或推测正确坐标。","来源地点冲突：待修正");
        var uncertainEventCount=planningCatalog.Count(SeasonalEventGuard.NeedsVerifiedDates);
        if(uncertainEventCount>0)context.Record("核对限时活动",$"候选中有 {uncertainEventCount} 个疑似季节性或限时活动。地图搜索结果不能证明本次日期开放，已排除自动推荐；没有推断活动停业或过期。","保守候选校验：活动日期待核实");
        if(candidateDays is not null) {
            var known=planningCatalog.Select(p=>p.Str("id")).ToHashSet(StringComparer.Ordinal);
            if(context.Walking is null || candidateDays.Count!=profile.Int("days"))throw new ServiceException("逐日步行候选不完整，请重新生成。");
            var groups=new List<HashSet<string>>();
            foreach(var value in candidateDays) {
                if(value is not JsonArray ids || ids.Count>30)throw new ServiceException("逐日步行候选不合法。");
                var group=new HashSet<string>(StringComparer.Ordinal);
                foreach(var node in ids) {
                    if(node is not JsonValue v || !v.TryGetValue<string>(out var id)||!known.Contains(id)||!group.Add(id))throw new ServiceException("逐日步行候选包含无效地点。");
                }
                groups.Add(group);
            }
            if(!profile.Array("mustVisit")!.All(id=>groups.Any(g=>g.Contains(id!.GetValue<string>()))))throw new ServiceException("步行候选遗漏了必去地点。");
            context.DayCandidateIds=groups;
            context.Record("按片区核对道路","先筛选每天的主题活动与附近备选，再用真实步行时间比较当天顺序；未穷举全城路线。","本地推荐规则 + Apple Maps");
        }

        if(raw.Array("walkingCandidateIds") is {} selected) {
            var ids=selected.Select(x=>x?.GetValue<string>()??"").ToHashSet();
            if(context.Walking is null || ids.Count==0 || ids.Any(id=>!planningCatalog.Any(p=>p.Str("id")==id)) || profile.Array("mustVisit")!.Any(x=>!ids.Contains(x!.GetValue<string>())))throw new ServiceException("候选地点不完整，请重新查询步行路线。");
            candidates=candidates.Where(p=>ids.Contains(p.Str("id"))).ToArray();
            context.Record("初筛步行候选",$"比较 {candidates.Count} 个候选地点的有方向道路耗时；保留必去地点。","Apple Maps / MapKit 客户端道路查询");
        }
        profile.Remove("walkingCandidateIds");
        profile.Remove("useAgent");
        var forecast = await WeatherForProfileAsync(profile, context, ct);
        var stays=profile.Str("accommodationStatus")=="booked"?[]:lists?.Array("accommodations")?.OfType<JsonObject>().ToArray()??[];
        var choice = useAgent ? await (agent??throw new ServiceException("旅行 Agent 尚未启动。")).ProposeAsync(profile,candidates,forecast,stays,ct):await recommendations.SelectAsync(profile, candidates, forecast, context, ct);
        if(useAgent) {
            context.Record("Koko Agent 推荐",choice.Str("explanation"),agent!.Provider=="meterless-cloud"?"Meterless H-MEM + NVIDIA / Skill "+ItinerarySkill.Version:agent.Provider=="meterless"?"Meterless Runtime / Skill "+ItinerarySkill.Version:"NVIDIA NIM / "+secrets.Read().GetValueOrDefault("NVIDIA_MODEL",SecretStore.DefaultModel));
            if(agent.UsesSkill)context.Record("檢索已確認偏好",$"H-MEM 返回 {choice.Int("confirmedMemoryCount")} 筆已確認偏好；本次需求優先，沒有自動保存新偏好。","Meterless H-MEM");
            foreach(var day in choice.Array("dayPlans")?.OfType<JsonObject>()??[])context.AgentDayRanks[day.Str("date")]=day.Array("preferredIds")!.Select((id,index)=>(Id:id!.GetValue<string>(),Index:index)).ToDictionary(x=>x.Id,x=>x.Index);
        }
        var ranks = choice.Array("preferredIds")!.Select((id, index) => (id: id!.GetValue<string>(), index))
            .ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);
        if (!shortlist && TravelAdjustmentIntent.Lighter(profile.Str("request")))
            profile["maxSteps"] = Math.Min(profile.Int("maxSteps"), 8000);
        var days = await scheduler.ScheduleDaysAsync(profile, candidates, forecast, ranks, context, ct);
        context.Record("交通方式比较",context.CompareTransit ? "在候选道路上比较 TfNSW 班次、等候、接驳步行和换乘。票价未知。":"用户选择只步行或兼容旧请求。","交通规划器");
        var draft=BuildDraft(profile, days, forecast, context);
        if(useAgent) {
            draft["planningAgent"]=new JsonObject{["provider"]=agent!.Provider,["status"]="draft_pending_verification",["skillVersion"]=agent.UsesSkill?ItinerarySkill.Version:null,["explanation"]=choice.Str("explanation"),["outstandingChecks"]=choice.Array("outstandingChecks")?.DeepClone()??new JsonArray(),["memoryEngine"]=choice["memoryEngine"]?.DeepClone(),["confirmedMemoryCount"]=choice.Int("confirmedMemoryCount")};
            if(agent.UsesSkill) {
                draft["accommodationSuggestions"]=AccommodationSuggestions.Select(profile,stays,choice.Array("accommodationIds"),days);
                draft.Array("warnings")!.Add("住宿僅為未預訂候選，未確認空房或實際房價；未變更行程起訖點，亦未計入活動預算。天氣警報、無障礙、營業與預約仍須確認。");
            }
        }
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
        if(imported is not null){draft["planningCandidates"]=new JsonArray(planningCatalog.Where(PoiService.IsImported).Select(p=>p.DeepClone()).ToArray());draft.Array("warnings")!.Add("地点来自本次真实搜索；未知门票、营业时间和餐饮价格未核实，预算可能不完整。");}
        if(context.MeterlessPlanning)draft["planningState"]=PlanningWorldState.Project(draft,false);
        return draft;
    }

    // This shortlist is not a final itinerary: the client must verify its directed road legs.
    public async Task<JsonObject> PrepareWalkingAsync(JsonObject raw,CancellationToken ct=default)
    {
        var input=(JsonObject)raw.DeepClone();input.Remove("mode");input.Remove("walkingRoutes");input.Remove("walkingDayCandidateIds");input.Remove("walkingCandidateIds");
        // A far must-see must reach the transit comparison instead of being rejected by a walking-only prefilter.
        if(input.Str("transportPreference","walk")!="walk"){input["maxSteps"]=60000;input["endMinute"]=1439;}
        var seed=await PlanAsync(input,"demo",ct,shortlist:true);
        var profile=seed.Object("profile")!;var eligible=TravelConstraints.Apply((JsonObject)profile.DeepClone(),PlanningCandidates.Merge(Catalog,seed.Array("planningCandidates")??raw.Array("planningCandidates"))).Where(p=>!PlanningInventory.IsAccommodation(p)).ToArray();
        var days=seed.Array("days")!.OfType<JsonObject>().ToArray();
        if(raw.Array("planningCandidates") is not null)eligible=eligible.Where(PoiService.IsImported).ToArray();
        var assigned=days.SelectMany(d=>d.Array("stops")!.OfType<JsonObject>().Where(p=>!RouteLogistics.IsTask(p))).Select(p=>p.Str("id")).ToHashSet();
        var groups=new JsonArray();var interests=profile.Array("interests")!.Select(i=>i!.GetValue<string>()).ToArray();
        foreach(var day in days) {
            var picks=day.Array("stops")!.OfType<JsonObject>().Where(p=>!RouteLogistics.IsTask(p)).ToArray();var ids=picks.Select(p=>p.Str("id")).ToList();
            if(picks.Length>0) {
                var backup=eligible.Where(p=>!assigned.Contains(p.Str("id")))
                    .OrderByDescending(p=>RecommendationQuality.InterestScore(p,interests)-picks.Min(s=>RouteService.Distance(s,p))/100)
                    .ThenBy(p=>p.Str("id"),StringComparer.Ordinal).FirstOrDefault();
                if(backup is not null)ids.Add(backup.Str("id"));
            }
            groups.Add(new JsonArray(ids.Select(id=>JsonValue.Create(id)).ToArray()));
        }
        return new JsonObject{["logisticsPoints"]=RouteLogistics.Points(profile),["profile"]=profile.DeepClone(),["candidates"]=seed["planningCandidates"]?.DeepClone(),["candidateDays"]=groups,["planningAgent"]=seed["planningAgent"]?.DeepClone(),["accommodationSuggestions"]=seed["accommodationSuggestions"]?.DeepClone(),["source"]=seed["planningAgent"] is not null?"Koko Agent 从真实搜索候选初筛，尚未完成道路与班次校验":"本地主题与片区初筛，尚未完成道路校验"};
    }

    private static JsonObject BuildDraft(JsonObject profile, JsonArray days, JsonObject weather, PlanningContext context)
    {
        context.Record("独立验证整条路线", "逐段检查步行、参观、10分钟衔接、15分钟休息、返程、估算花费和营业时间；无法验证的项目明确列出。", "确定性规则引擎");
        var warnings = new JsonArray
        {
            "花费、场内步数与停留时间为估算；未包含酒店、交通票价、全部餐饮或特殊展门票。",
            context.CompareTransit?"已比较步行与公共交通；未评估无障碍、坡度、排队与道路临时封闭。":"步行模式；未评估无障碍、坡度、排队与道路临时封闭。"
        };
        if (profile.Array("mustVisit")!.Count>0 && !string.IsNullOrEmpty(profile.Str("mustDo")))warnings.Add("特别想去／想做："+profile.Str("mustDo")+"。已作为必去约束检查并排入路线。");
        if (!string.IsNullOrEmpty(profile.Str("avoid")) && profile.Str("avoid") is not ("无" or "未确定" or "暂不确定" or "无特别要求"))warnings.Add("避开／出行需求："+profile.Str("avoid")+"。已按可识别地点和类别筛选；无障碍、临时封路等仍需另行核实。");
        if (!string.IsNullOrEmpty(profile.Str("origin")) && profile.Str("origin") != profile.Object("start")!.Str("name"))warnings.Add("出发地点备注："+profile.Str("origin")+"；当前起点为"+profile.Object("start")!.Str("name")+"，请核实位置。");
        if (!string.IsNullOrEmpty(profile.Str("queueTolerance")))warnings.Add("排队偏好已记录，但缺少可靠实时排队数据，无法保证排队时长。");
        if (days.OfType<JsonObject>().Any(d=>d.Bool("freeTime")))
            warnings.Add("部分日期为自由安排：现有候选景点无法在预算、步数及营业时间约束下每天安排不重复活动，请检查日程并补充地点。");
        if (weather.Bool("rainy") && days.OfType<JsonObject>()
                .SelectMany(x => x.Array("stops")!.OfType<JsonObject>()).Any(x => !x.Bool("indoor")))
            warnings.Add("行程包含户外活动，请出发前确认天气并准备室内替代安排。");
        if(context.CompareTransit)warnings.Add("已比较步行与 NSW 公共交通；班次是查询时结果，出发前需刷新。票价未提供，预算不包含公交费用。");
        if(context.MeterlessPlanning)warnings.Insert(0,JsonValue.Create(context.Walking is not null?"Meterless 已提供候選建議，步行依 Apple 道路資料核對；住宿、費用、營業與天氣警報仍有待確認項目。":"Meterless 已提供候選建議；交通仍依本次路線模式的資料驗證或估算，不代表所有限制均已確認。"));
        else if(context.Walking is not null)warnings.Insert(0,JsonValue.Create("步行已按 Apple 道路查询验证；地点费用与未核实营业时间仍为估算。"));
        else if (context.Mode == "demo") warnings.Insert(0, JsonValue.Create("演示模式：未调用 Google 或 Nemotron；路线不是导航路径。"));
        else if (context.Mode == "assisted") warnings.Insert(0, JsonValue.Create("Nemotron 已真实调用；地点、营业时段与路线仍为示例估算；天氣以畫面標示來源為準，未查询 Google。"));
        var allStops=days.OfType<JsonObject>().SelectMany(d=>d.Array("stops")!.OfType<JsonObject>()).ToArray();
        var unmet=profile.Array("interests")!.Select(i=>i!.GetValue<string>()).Where(i=>!allStops.Any(p=>RecommendationQuality.Matches(p,i))).ToArray();
        foreach(var interest in unmet)warnings.Add("未满足「"+interest+"」偏好：当前候选或出行限制不足，没有用其他活动冒充。");
        foreach(var day in days.OfType<JsonObject>())foreach(var w in day.Array("warnings")?.OfType<JsonValue>()??[]) {
            var text=w.GetValue<string>();if(text.Contains("未覆盖")||text.Contains("未安排具体餐厅")||text.Contains("半日活动")||text.Contains("未能填滿")||text.Contains("準時用餐")||text.Contains("行李寄放服務"))warnings.Add(day.Str("date")+"："+text);
        }
        var partial=unmet.Length>0||days.OfType<JsonObject>().Any(d=>d.Bool("freeTime")||d.Str("recommendationStatus")=="partial");
        if(allStops.Any(RecommendationQuality.IsMeal))warnings.Add("具体餐厅已安排；餐费为每人估算并计入本日预算，不代表菜单报价、订位或有空位。营业时间出发前仍需确认。");
        var explanation = $"共安排 {allStops.Length} 项活动，其中 {allStops.Count(RecommendationQuality.IsMeal)} 次具体用餐。每天 A${profile.Number("budget").ToString("G", CultureInfo.InvariantCulture)} 活动预算和 {profile.Int("maxSteps")} 步上限，规划 {days.Count} 天路线。已计入交通、参观、休息和返回{profile.Object("end")!.Str("name")}的时间。";
        if (weather.Bool("rainy")) explanation += context.MeterlessPlanning?"预报有雨，结合兴趣、室内外偏好及天气接受程度比较安排；出发前确认天气。":"预报有雨，优先室内，缩短可选户外停留；出发前确认天气。";
        if(days.OfType<JsonObject>().Any(d=>d.Array("warnings")!.Any(w=>w!.GetValue<string>().Contains("半日活动"))))explanation+="部分日期仅安排半日活动，下午留白，未视为全天已排满。";
        if(partial)explanation="这是一份待补充草案，部分需求尚未满足。"+explanation;
        return new JsonObject
        {
            ["recommendationStatus"]=partial?"partial":"ready",["title"] = "悉尼小冒险", ["mode"] = context.Mode, ["profile"] = profile, ["days"] = days,
            ["dataSource"] = weather.Str("dataSource","legacy"), ["weather"] = weather, ["explanation"] = explanation, ["warnings"] = warnings,
            ["trace"] = context.Trace.DeepClone(), ["createdAt"] = context.Now
        };
    }

    private async Task<JsonObject> WeatherForProfileAsync(JsonObject profile, PlanningContext context, CancellationToken ct)
    {
        var value = (travel?.Explicit==true || agent?.UsesSkill==true) && publicWeather is not null ? await publicWeather.GetAsync(profile,ct) : context.Mode == "live"
            ? await weather.GetAsync(profile, context.Config.GetValueOrDefault("GOOGLE_SERVER_KEY"), ct)
            : new JsonObject { ["summary"] = "晴天（演示）", ["rainy"] = false, ["source"] = "演示假设" };
        if (profile.Str("weatherOverride") == "rain")
            value = new JsonObject { ["summary"] = "下雨情景测试（用户指定）", ["rainy"] = true, ["source"] = "用户情景，非实时天气" };
        context.Record("检查天气", value.Str("summary"), value.Str("source"));
        return value;
    }

    // 兼容已有领域测试的入口；实现职责分别属于推荐、调度和路段服务。
    // Preserve existing test entry points while delegating to focused owners.
    public static JsonObject ParseModelJson(string content) => RecommendationService.ParseModelJson(content);
    public static IReadOnlyList<(int Open, int Close)> OpeningWindows(JsonObject place, string date, out bool verified) =>
        RouteScheduler.OpeningWindows(place, date, out verified);
    public static double Distance(JsonObject a, JsonObject b) => RouteService.Distance(a, b);
}
