using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// Shared product rules: an area is not a meal, and passing route constraints is not recommendation quality.
public static class RecommendationQuality
{
    public static bool IsMeal(JsonObject place) => place.Str("activityType") == "meal";
    public static bool Matches(JsonObject place, string interest) => interest=="美食" ? IsMeal(place) : place.Str("category") == interest ||
        (place.Array("tags")?.Any(t => t?.GetValue<string>() == interest) ?? false) ||
        (interest == "文化" && place.Str("category") == "艺术") ||
        (interest == "购物" && place.Str("id") == "qvb");
    public static (int Open,int Close)? MealWindow(JsonObject profile) =>
        profile.Int("startMinute") < 840 && profile.Int("endMinute") >= 750 ? (690,870) :
        profile.Int("startMinute") < 1200 && profile.Int("endMinute") >= 1110 ? (1020,1260) :
        profile.Int("startMinute") < 660 && profile.Int("endMinute")>=profile.Int("startMinute")+60 ? (Math.Max(480,profile.Int("startMinute")),Math.Min(690,profile.Int("endMinute"))) : null;
    public static double InterestScore(JsonObject p, IEnumerable<string> interests) => interests.Count(i => Matches(p,i))*24d;
    public static JsonObject[] DailyPool(IEnumerable<JsonObject> places, JsonObject profile, IReadOnlySet<string> must,
        IReadOnlyDictionary<string,int> ranks)
    {
        var interests=profile.Array("interests")!.Select(x=>x!.GetValue<string>()).ToArray();
        double Rank(JsonObject p) => InterestScore(p,interests) + (MealWindow(profile)!=null && IsMeal(p)?18:0)
            + (ranks.Count-ranks.GetValueOrDefault(p.Str("id"),ranks.Count))*0.8
            - RouteService.Distance(profile.Object("start")!,p)/250;
        var all=places.Where(p=>must.Contains(p.Str("id")) || !DuplicatesEndpoint(p,profile)).OrderByDescending(Rank).ThenBy(p=>p.Str("id"),StringComparer.Ordinal).ToArray();
        var result=all.Where(p=>must.Contains(p.Str("id"))).ToList();
        // Keep both genuine meals and visits in the bounded search, even for a food-only trip.
        foreach(var p in all.Where(IsMeal).Take(3).Concat(all.Where(p=>!IsMeal(p)).Take(5)))
            if(result.All(x=>x.Str("id")!=p.Str("id")))result.Add(p);
        return result.ToArray();
    }
    public static bool DuplicatesEndpoint(JsonObject place,JsonObject profile) {
        static string Name(string raw) {
            var s=System.Text.RegularExpressions.Regex.Replace(raw.ToLowerInvariant(),@"[\s，,、。；;！!]+","");
            return s switch {"环形码头" or "環形碼頭" or "circularquay"=>"circularquay","中央车站" or "中央車站" or "centralstation" or "sydneycentralstation"=>"centralstation",_=>s};
        }
        return new[]{profile.Object("start"),profile.Object("end")}.Where(p=>p is not null).Any(p=>
            RouteService.Distance(place,p!)<50 && Name(place.Str("name")).Length>0 && Name(place.Str("name"))==Name(p!.Str("name")));
    }
    public static void Assess(JsonObject profile,JsonArray days)
    {
        var interests=profile.Array("interests")!.Select(x=>x!.GetValue<string>()).ToArray();
        foreach(var day in days.OfType<JsonObject>()) {
            var stops=day.Array("stops")!.OfType<JsonObject>().ToArray();var warnings=day.Array("warnings")!;
            if(stops.Length==0)continue;
            if(interests.Length>0&&!stops.Any(p=>interests.Any(i=>Matches(p,i))))
                warnings.Add("当天未覆盖你的核心兴趣：当前地点、步数或预算不足，不能视为已满足偏好。");
            if(MealWindow(profile)!=null&&!stops.Any(IsMeal))warnings.Add("当天未安排具体餐厅：现有餐饮候选不满足预算、步数或时间条件，请另行安排用餐。");
            var finish=day.Str("finishTime");var shortDay=profile.Int("endMinute")-profile.Int("startMinute")>=360 && string.CompareOrdinal(finish,"14:00")<0;
            if(shortDay)warnings.Add("当前仅安排了半日活动，余下时间留白；按轻松节奏及现有地点、步数和预算限制，没有凑满全天。");
            var incompleteDay=shortDay&&(profile.Str("dayStyle")=="fullDay" || (profile.Str("energy")!="低精力"&&profile.Str("rhythm")!="轻松随意"));
            day["recommendationStatus"]=incompleteDay||warnings.OfType<JsonValue>().Any(w=>w.GetValue<string>().Contains("未覆盖")||w.GetValue<string>().Contains("未安排具体餐厅"))?"partial":"ready";
        }
    }
}
