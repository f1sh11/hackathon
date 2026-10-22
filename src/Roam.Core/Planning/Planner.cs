namespace Roam.Core;

/// <summary>
/// A bounded deterministic beam search over combinations and ordered visits. No external AI/API calls.
/// The search checks every prefix and its return leg, so later recovery cannot rescue an exhausted route.
/// </summary>
public sealed class Planner
{
    public const string DataNotice = "示例数据：地点费用、开放时段、步行、转场和 HP 均为演示估算；未接入实时地图、节假日营业、天气或预订。HP 不是身体测量。";
    private const int BeamPerBreakGroup = 64;
    private static readonly SamplePlace[] Catalog =
    [
        new("harbour", "环形码头 · 海港漫步", "海港风景", ["harbour"], 65, 0m, 1.1m, .5, .3, 0, 25, 15, 420, 1260, false, false, 0, "把第一站留给海风，慢慢看看海港。"),
        new("garden", "植物园 · 绿荫散步", "户外漫游", ["harbour", "nature"], 65, 0m, 1.5m, 1.1, .5, 1, 45, 25, 480, 1080, false, false, 0, "沿绿荫慢走，给拍照和停留留一些时间。"),
        new("rocks", "岩石区 · 街巷闲逛", "城市街区", ["harbour", "culture"], 70, 0m, 1.2m, .4, .7, 1, 55, 30, 540, 1080, false, false, 0, "看看街巷与小店，走累了就停一停。"),
        new("gallery", "城市美术馆 · 作品与光线", "文化参观", ["culture"], 90, 20m, .8m, 1.1, .1, 0, 35, 10, 570, 1020, false, false, 0, "慢慢看展，不必把每一个展厅都变成任务。"),
        new("museum", "城市博物馆 · 故事与展陈", "文化参观", ["culture"], 85, 18m, .7m, .5, -.4, 0, 45, 15, 600, 1020, false, false, 0, "选择感兴趣的部分，留时间理解一段城市故事。"),
        new("library", "城市图书馆 · 安静一小时", "安静文化", ["culture"], 60, 0m, .3m, .2, -.1, 0, 65, 10, 600, 1020, false, false, 0, "在书架与建筑之间慢慢逛，不追求打卡数量。"),
        new("waterfront", "滨水步道 · 城市另一面", "滨水漫游", ["harbour", "nature"], 70, 0m, 1.6m, -1.0, -.2, 0, 60, 25, 420, 1200, false, false, 0, "沿水边换一个角度看城市，给散步留一点空白。"),
        new("park", "城市公园 · 草坪小憩", "自然绿地", ["nature"], 60, 0m, .8m, .5, -.2, 0, 25, 20, 420, 1200, false, false, 0, "在草坪附近轻松走走，看看树影。"),
        new("lookout", "山坡观景点 · 登高远望", "登高风景", ["nature", "harbour"], 85, 12m, 2.3m, 2.1, 1.1, 3, 85, 60, 480, 1020, false, false, 0, "需要更多上坡与步行，适合愿意登高的旅行者。"),
        new("studio", "小众工作室 · 城市手作", "小众探索", ["culture"], 80, 32m, .5m, -.8, -.7, 0, 90, 50, 660, 1020, false, false, 0, "给小众体验留一段完整的时间。"),
        new("market", "街区市集 · 慢慢发现", "街区探索", ["culture", "harbour"], 70, 10m, .9m, -.5, .8, 1, 70, 55, 600, 960, false, false, 0, "看看摊位与街区，费用按演示体验预算计入。"),
        new("reserve", "绿地保留区 · 林荫小径", "自然探索", ["nature"], 85, 0m, 2m, 1.8, -.4, 2, 80, 45, 480, 1020, false, false, 0, "给喜欢自然的人安排较长的林荫步行。"),
        new("lunch", "咖啡与简餐 · 中途休息", "用餐休息", ["harbour", "culture", "nature"], 60, 26m, .1m, .4, .1, 0, 25, 10, 690, 900, true, true, 18, "坐下来吃点东西，给下一段安排留些余力。"),
        new("picnic", "自备简餐 · 公园休息", "用餐休息", ["nature", "harbour", "culture"], 50, 8m, .1m, .6, -.1, 0, 45, 20, 690, 900, true, true, 15, "按演示预算计入简餐与补给，在公园附近坐下休息。"),
        new("coffee", "街角咖啡 · 坐下来一会儿", "咖啡休息", ["harbour", "culture", "nature"], 35, 7m, .1m, .1, .2, 0, 35, 10, 480, 1080, true, false, 12, "喝杯咖啡，暂时把步行和打卡放到一边。"),
        new("unverified", "待核实的特别展览", "信息待确认", ["culture"], 70, 15m, .4m, .4, .4, 0, 90, 90, null, null, false, false, 0, "开放时间尚未确认，不参与可执行方案生成。")
    ];

    public IReadOnlyList<SamplePlace> Places { get; } = Array.AsReadOnly(Catalog);

    public PlanningResult Generate(TravelerProfile profile, PlanningRequest request)
    {
        Validate(profile, request);
        var values = profile.Values;
        var baseline = request.Easier ? Generate(profile, request with { Easier = false }).Plan : null;
        var target = values[2] < 40 ? 2 : values[2] > 70 ? 4 : 3;
        var maxAttractions = request.Easier ? Math.Max(1, target - 1) : target;
        var walkLimit = (2m + values[0] * .11m) * (request.Easier ? .72m : 1m);
        var reserve = (int)Math.Ceiling(request.Energy * (request.Easier ? .40 : .25));
        var start = ToMinutes(request.StartTime);
        var deadline = ToMinutes(request.EndTime);
        var horizon = request.Easier ? start + (int)((deadline - start) * .8) : deadline;
        if (baseline is not null)
        {
            maxAttractions = Math.Min(maxAttractions, Math.Max(1, baseline.Stops.Count(stop => stop.Id != "return" && !Catalog.First(place => place.Id == stop.Id).IsBreak) - 1));
            reserve = Math.Max(reserve, baseline.RemainingHp);
            walkLimit = Math.Min(walkLimit, baseline.WalkKm);
            horizon = Math.Min(horizon, ToMinutes(TimeOnly.Parse(baseline.ReturnTime)));
        }
        var initial = new Node([], null, start, request.Energy, 0m, 0m, 0, false, false);
        var frontier = new List<Node> { initial };
        Node? best = null;
        double bestScore = double.NegativeInfinity;

        for (var depth = 0; depth <= maxAttractions + 1; depth++)
        {
            var next = new List<Node>();
            foreach (var node in frontier)
            {
                var completed = TryFinish(node, request, reserve, walkLimit, horizon, values);
                if (completed is not null && (baseline is null || IsMeaningfullyEasier(completed, baseline, values)))
                {
                    var score = Score(completed, profile, request);
                    if (score > bestScore)
                    {
                        best = completed;
                        bestScore = score;
                    }
                }
                if (depth == maxAttractions + 1) continue;
                foreach (var place in Catalog)
                {
                    if (place.OpensAt is null || place.ClosesAt is null) continue;
                    if (node.Visits.Any(visit => visit.Place.Id == place.Id)) continue;
                    if (place.IsBreak && node.HasBreak) continue;
                    if (!place.IsBreak && node.Attractions >= maxAttractions) continue;
                    if (place.IsBreak && node.Attractions == 0) continue;
                    if (place.Slope > (values[1] < 40 ? 1 : values[1] > 70 ? 3 : 2)) continue;
                    var expanded = TryVisit(node, place, request, reserve, walkLimit, horizon, values);
                    if (expanded is not null) next.Add(expanded);
                }
            }
            // Preserve both meal and no-meal branches so a worthwhile lunch isn't crowded out by attractions.
            frontier = next.GroupBy(node => node.HasMeal)
                .SelectMany(group => group.OrderByDescending(node => Score(node, profile, request)).Take(BeamPerBreakGroup))
                .ToList();
            if (frontier.Count == 0) break;
        }

        if (best is null)
        {
            var conflicts = new List<string>();
            if (deadline - start < 90) conflicts.Add("可用时间不足，示例方案需要容纳停留、转场和返程。请延长时间。");
            if (request.Energy < 25) conflicts.Add("当前精力很低，无法在保留余力的前提下完成示例路线。请缩短活动或调整出行安排。");
            if (request.Budget < 8 && start >= 15 * 60) conflicts.Add("当前时段的免费地点较少，预算不足以安排可行的示例方案。");
            if (start >= 19 * 60) conflicts.Add("多数示例地点已结束开放，当前数据无法安排夜间行程。");
            if (request.Easier && baseline is not null) conflicts.Add("在保留当前精力与预算条件下，没有找到明显更轻松的替代路线。可以保留原方案或修改本次任务。");
            if (conflicts.Count == 0) conflicts.Add("在当前预算、开放时段、步行与精力限制下，没有找到可行组合。请放宽其中一项后重试。");
            return new PlanningResult(false, conflicts.ToArray(), null);
        }

        var stops = best.Visits.Select(visit => new ItineraryStop(
            visit.Place.Id, visit.Place.Name, visit.Place.Category,
            Format(visit.Begin), Format(visit.End), visit.Transfer.Minutes,
            visit.End - visit.Begin, visit.Place.Cost + visit.Transfer.Cost,
            decimal.Round(visit.Place.OnsiteWalkKm + visit.Transfer.WalkKm, 2), visit.Hp,
            visit.Place.Description)).ToList();
        var lastReturn = Travel(best.Last, null, values);
        stops.Add(new ItineraryStop("return", "返回出发点", "返程", Format(best.Time),
            Format(best.Time + lastReturn.Minutes), lastReturn.Minutes, 0,
            lastReturn.Cost, lastReturn.WalkKm, best.Hp - lastReturn.Hp,
            "已计入返回出发点的时间、交通费用与预计体力；转场均为本地示例估算。"));
        var totalCost = best.Cost + lastReturn.Cost;
        var totalWalk = decimal.Round(stops.Sum(stop => stop.WalkKm), 2);
        var finalHp = best.Hp - lastReturn.Hp;
        var reasons = new List<string>
        {
            $"按你的节奏保留 {best.Attractions} 个重点，所有转场和返程都已计入。",
            $"预计支出 A${totalCost:0.##}，在 A${request.Budget:0.##} 预算内；结束时保留 HP {finalHp}。",
            values[1] < 40 ? "优先选择平缓地点，避开高坡度示例路线。" : "按照你的爬坡与步行接受度筛选路线。",
            best.HasMeal ? "安排了中途简餐，预计恢复部分精力；恢复不会弥补此前的体力透支。" : best.HasBreak ? "安排了中途休息，给下一段行程留些余力。" : "当前路线较短，保留提前返回后的自由时间。"
        };
        if (request.Easier) reasons.Insert(0, "轻松版减少景点上限，并提高精力余量、收紧步行与时间上限。");
        if (!string.IsNullOrWhiteSpace(request.Notes)) reasons.Add("备注已保存；离线版本只按结构化选项计算，不会解析自由文本。");
        return new PlanningResult(true, [], new TripPlan(stops.ToArray(), finalHp, totalCost,
            totalWalk, Format(best.Time + lastReturn.Minutes), reasons.ToArray(), DataNotice));
    }

    private static Node? TryVisit(Node node, SamplePlace place, PlanningRequest request,
        int reserve, decimal walkLimit, int horizon, int[] values)
    {
        var transfer = Travel(node.Last, place, values);
        var arrival = node.Time + transfer.Minutes;
        var begin = Math.Max(arrival, place.OpensAt!.Value);
        if (place.IsMeal) begin = Math.Max(begin, 690);
        if (begin - arrival > (place.IsMeal ? 60 : 40)) return null;
        if (place.IsMeal && begin > 855) return null;
        var duration = place.DurationMinutes + (!place.IsBreak && values[2] < 40 ? 15 : 0);
        var end = begin + duration;
        if (end > place.ClosesAt!.Value || end > horizon) return null;
        var afterTransferHp = node.Hp - transfer.Hp;
        if (afterTransferHp <= 0) return null;
        var exertion = place.IsBreak ? 0 : (int)Math.Ceiling(6 + (double)place.OnsiteWalkKm * (2.8 - values[0] * .012)
            + place.Slope * (3.2 - values[1] * .018) + duration * .035 + (begin - arrival) * .015);
        var hp = afterTransferHp - exertion;
        if (hp <= 0) return null;
        if (place.IsBreak) hp = Math.Min(request.Energy, hp + place.RecoveryHp);
        var cost = node.Cost + transfer.Cost + place.Cost;
        var walk = node.WalkKm + transfer.WalkKm + place.OnsiteWalkKm;
        var back = Travel(place, null, values);
        if (cost + back.Cost > request.Budget || walk + back.WalkKm > walkLimit ||
            end + back.Minutes > horizon || hp - back.Hp < reserve) return null;
        var visit = new Visit(place, transfer, begin, end, hp);
        return new Node([.. node.Visits, visit], place, end, hp, cost, walk,
            node.Attractions + (place.IsBreak ? 0 : 1), node.HasBreak || place.IsBreak, node.HasMeal || place.IsMeal);
    }

    private static Node? TryFinish(Node node, PlanningRequest request, int reserve, decimal walkLimit, int horizon, int[] values)
    {
        if (node.Attractions == 0) return null;
        var back = Travel(node.Last, null, values);
        if (node.Time + back.Minutes > horizon || node.Cost + back.Cost > request.Budget ||
            node.WalkKm + back.WalkKm > walkLimit) return null;
        var start = ToMinutes(request.StartTime);
        var end = node.Time + back.Minutes;
        var spansLunch = start <= 810 && end >= 750 && end - start >= 240;
        if (spansLunch && !node.HasMeal) return null;
        var longTrip = end - start >= 300;
        if (longTrip && !node.HasBreak) return null;
        if (node.Hp - back.Hp < reserve) return null;
        return node;
    }

    private static bool IsMeaningfullyEasier(Node node, TripPlan baseline, int[] values)
    {
        var back = Travel(node.Last, null, values);
        var baselineAttractions = baseline.Stops.Count(stop => stop.Id != "return" && !Catalog.First(place => place.Id == stop.Id).IsBreak);
        return node.Attractions < baselineAttractions || node.WalkKm + back.WalkKm <= baseline.WalkKm - .1m
            || node.Time + back.Minutes <= ToMinutes(TimeOnly.Parse(baseline.ReturnTime)) - 15
            || node.Hp - back.Hp >= baseline.RemainingHp + 3;
    }

    private static double Score(Node node, TravelerProfile profile, PlanningRequest request)
    {
        var v = profile.Values;
        double score = node.Attractions * 110 + (node.HasMeal ? 22 : node.HasBreak ? 6 : 0);
        foreach (var visit in node.Visits.Where(visit => !visit.Place.IsBreak))
        {
            var place = visit.Place;
            score += place.Themes.Contains(request.Theme) ? 38 : 0;
            score += 20 - Math.Abs(place.Exploration - v[4]) * .25;
            score -= Math.Max(0, place.Uncertainty - v[5]) * .3;
            // High spending willingness permits paid experiences; a high budget is not an instruction to spend it.
            score += place.Cost > 0 ? (v[3] - 50) * .22 : (100 - v[3]) * .08;
        }
        score -= (double)node.Cost * (v[3] < 40 ? .5 : .07);
        score -= (request.Energy - node.Hp) * (request.Easier ? 1.1 : .18);
        score -= (double)node.WalkKm * (request.Easier ? 4 : .5);
        score -= (node.Time - ToMinutes(request.StartTime)) * .012;
        return score;
    }

    private static Leg Travel(SamplePlace? from, SamplePlace? to, int[] values)
    {
        var dx = (to?.X ?? 0) - (from?.X ?? 0);
        var dy = (to?.Y ?? 0) - (from?.Y ?? 0);
        var distance = Math.Sqrt(dx * dx + dy * dy) * 1.25;
        if (distance < .01) return new Leg(0, 0m, 0m, 0);
        var walking = distance <= .8;
        var km = decimal.Round(walking ? (decimal)distance : .25m, 2);
        var minutes = walking ? Math.Max(5, (int)Math.Ceiling(distance / 4 * 60)) : 10 + (int)Math.Ceiling(distance / 18 * 60);
        var hp = (int)Math.Ceiling((double)km * (3.0 - (values.Length == 6 ? values[0] : 25) * .01));
        return new Leg(minutes, walking ? 0m : 3.2m, km, hp);
    }

    private static void Validate(TravelerProfile profile, PlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        if (profile.Answers is null || profile.Answers.Length != 6 || profile.Answers.Any(answer => answer is < 0 or > 2)
            || profile.Values is null || profile.Values.Length != 6
            || !profile.Values.SequenceEqual(profile.Answers.Select(answer => new[] { 25, 55, 85 }[answer])))
            throw new ArgumentException("画像须由完整问答生成，不能手动调整数值。", nameof(profile));
        if (request.Date.Year is < 2000 or > 2100) throw new ArgumentException("请选择有效的出行日期。");
        if (request.EndTime <= request.StartTime) throw new ArgumentException("最晚返回时间需要晚于出发时间；当前版本只支持当天行程。");
        if (request.Budget < 0 || request.Budget > 100000) throw new ArgumentException("预算必须介于 0 和 100000 澳元之间。");
        if (request.Energy is < 10 or > 100) throw new ArgumentException("当日 HP 必须介于 10 和 100 之间。");
        if (request.Theme is not ("harbour" or "culture" or "nature")) throw new ArgumentException("请选择海港、文化或自然主题。");
        if (request.Notes?.Length > 500) throw new ArgumentException("旅行备注不能超过 500 字。");
    }

    private static int ToMinutes(TimeOnly time) => time.Hour * 60 + time.Minute;
    private static string Format(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
    private sealed record Leg(int Minutes, decimal Cost, decimal WalkKm, int Hp);
    private sealed record Visit(SamplePlace Place, Leg Transfer, int Begin, int End, int Hp);
    private sealed record Node(Visit[] Visits, SamplePlace? Last, int Time, int Hp, decimal Cost,
        decimal WalkKm, int Attractions, bool HasBreak, bool HasMeal);
}
