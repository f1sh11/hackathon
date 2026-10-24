using System.Globalization;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 规划用例编排：校验输入、收集证据、获得推荐、检查路线，再返回未保存草案。
/// Orchestrates validation, evidence, recommendations and scheduling; returns drafts without persistence.
/// </summary>
public sealed class Planner(PlaceService places, RecommendationService recommendations, RouteScheduler scheduler,
    WeatherService weather, SecretStore secrets)
{
    public IReadOnlyList<JsonObject> Catalog => places.Catalog;

    /// <summary>输入行程日期/时间，输出天气状态；Input: travel profile. Output: explicit forecast availability.</summary>
    public async Task<JsonObject> WeatherAsync(JsonObject raw, CancellationToken ct = default)
    {
        var context = new PlanningContext("live", secrets.Read());
        return await WeatherForProfileAsync(PlanValidator.Validate(raw, Catalog), context, ct);
    }

    /// <summary>
    /// 输入客户端约束与模式，返回兼容原客户端的未保存行程。
    /// Takes constraints and mode; returns an unsaved itinerary with the original mobile JSON contract.
    /// </summary>
    public async Task<JsonObject> PlanAsync(JsonObject raw, string mode, CancellationToken ct = default)
    {
        if (mode is not ("demo" or "assisted" or "live")) throw new ServiceException("模式不合法");
        var context = new PlanningContext(mode, secrets.Read());
        var profile = PlanValidator.Validate(raw, Catalog);
        var candidates = await places.GetCandidatesAsync(context, ct);
        var forecast = await WeatherForProfileAsync(profile, context, ct);
        var choice = await recommendations.SelectAsync(profile, candidates, forecast, context, ct);
        var ranks = choice.Array("preferredIds")!.Select((id, index) => (id: id!.GetValue<string>(), index))
            .ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);
        if (new[] { "少走", "累", "轻松" }.Any(x => profile.Str("request").Contains(x, StringComparison.Ordinal)))
            profile["maxSteps"] = Math.Min(profile.Int("maxSteps"), 7000);
        var days = await scheduler.ScheduleDaysAsync(profile, candidates, forecast, ranks, context, ct);
        return BuildDraft(profile, days, forecast, context);
    }

    private static JsonObject BuildDraft(JsonObject profile, JsonArray days, JsonObject weather, PlanningContext context)
    {
        context.Record("独立验证整条路线", "逐段检查步行、参观、10分钟衔接、15分钟休息、返程、估算花费和营业时间；无法验证的项目明确列出。", "确定性规则引擎");
        var warnings = new JsonArray
        {
            "花费、场内步数与停留时间为估算；未包含酒店、交通票价、全部餐饮或特殊展门票。",
            "步行模式；未评估无障碍、坡度、排队与道路临时封闭。"
        };
        if (!string.IsNullOrEmpty(profile.Str("mustDo")))warnings.Add("特别想去／想做："+profile.Str("mustDo")+"。当前示例规划尚未保证排入，请检查并编辑路线。");
        if (!string.IsNullOrEmpty(profile.Str("avoid")))warnings.Add("避开／出行需求："+profile.Str("avoid")+"。尚未自动核实，请逐项检查路线是否适合。");
        if (!string.IsNullOrEmpty(profile.Str("origin")) && profile.Str("origin") != profile.Object("start")!.Str("name"))warnings.Add("出发地点备注："+profile.Str("origin")+"；当前起点为"+profile.Object("start")!.Str("name")+"，请核实位置。");
        if (!string.IsNullOrEmpty(profile.Str("queueTolerance")))warnings.Add("排队偏好已记录，但缺少可靠实时排队数据，无法保证排队时长。");
        if (days.OfType<JsonObject>().Any(d=>d.Bool("freeTime")))
            warnings.Add("部分日期为自由安排：现有候选景点无法在预算、步数及营业时间约束下每天安排不重复活动，请检查日程并补充地点。");
        if (weather.Bool("rainy") && days.OfType<JsonObject>()
                .SelectMany(x => x.Array("stops")!.OfType<JsonObject>()).Any(x => !x.Bool("indoor")))
            warnings.Add("你选择的必去地点包含户外活动，请自行确认天气并准备替代安排。");
        if (context.Mode == "demo") warnings.Insert(0, JsonValue.Create("演示模式：未调用 Google 或 Nemotron；路线不是导航路径。"));
        else if (context.Mode == "assisted") warnings.Insert(0, JsonValue.Create("Nemotron 已真实调用；地点、天气、营业时段与路线仍为示例估算，未查询 Google。"));
        var explanation = $"按你的兴趣、每天 A${profile.Number("budget").ToString("G", CultureInfo.InvariantCulture)} 活动预算和 {profile.Int("maxSteps")} 步上限安排了 {days.Count} 天路线。已计入步行、参观、休息和返回{profile.Object("end")!.Str("name")}的时间。";
        if (weather.Bool("rainy")) explanation += "下雨情景优先安排室内地点，必去地点除外。";
        return new JsonObject
        {
            ["title"] = "悉尼小冒险", ["mode"] = context.Mode, ["profile"] = profile, ["days"] = days,
            ["weather"] = weather, ["explanation"] = explanation, ["warnings"] = warnings,
            ["trace"] = context.Trace.DeepClone(), ["createdAt"] = context.Now
        };
    }

    private async Task<JsonObject> WeatherForProfileAsync(JsonObject profile, PlanningContext context, CancellationToken ct)
    {
        var value = context.Mode == "live"
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
