using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Request-scoped search results. Never mutate the shared catalog or present unknown facts as verified.
public static class PlanningCandidates
{
    // Provider discovery groups are broad. Only actual source types establish an interest;
    // the query text and a place's name never prove a museum, gallery or natural attraction.
    private static string? InterestForKind(string kind) {
        if(kind is "art_gallery" or "gallery" or "art_museum" or "contemporary_art_museum" or "artwork")return "艺术";
        if(kind=="museum" || kind.EndsWith("_museum",StringComparison.Ordinal))return "文化";
        return kind switch {
            "park" or "garden" or "botanical_garden" or "national_park" or "state_park" or "city_park" or "nature_reserve" or "beach" or "waterfall" or "forest"=>"自然",
            "historic_site" or "memorial" or "monument" or "archaeological_site" or "heritage_site"=>"文化",
            _=>null
        };
    }
    public static IReadOnlyList<JsonObject> Merge(IReadOnlyList<JsonObject> catalog,JsonArray? input)
    {
        var all=catalog.Select(p=>p.DeepClone().AsObject()).ToList();
        if(input is null)return all;
        if(input.Count>60)throw new ServiceException("搜索候选过多，请缩小规划区域。");
        var seen=new HashSet<string>();
        foreach(var node in input) {
            if(node is not JsonObject value||!seen.Add(value.Str("id")))throw new ServiceException("搜索候选无效或重复。");
            var p=PoiService.Import(value);
            p["activityType"]=PoiService.ActivityKind(p)=="meal"?"meal":"attraction";
            var category=value.Str("category");
            if(category is "自然" or "文化" or "艺术" or "购物" or "城市" or "风景")p["category"]=category;
            if(p.Str("category")=="風景")p["category"]="风景";
            if(p.Str("category")=="店家")p["category"]="购物";
            if(p.Str("category")=="风景" && InterestForKind(p.Str("poiKind").ToLowerInvariant()) is {} interest)p["category"]=interest;
            p["tags"]=p.Str("category")=="自然" ? new JsonArray("自然","风景"):new JsonArray(p.Str("category"));p["indoorKnown"]=false;
            p["openMinute"]=0;p["closeMinute"]=1440;p["hoursVerified"]=false;p["costKnown"]=false;
            p["cost"]=p.Str("activityType")=="meal"?30:0;
            p["note"]="真实搜索地点；营业时间、门票／菜单价格和室内情况未核实。"+(p.Str("activityType")=="meal"?"每人餐费暂预留 A$30，非菜单报价。":"未计入未知门票，不代表免费。");
            if(all.Any(x=>x.Str("id")==p.Str("id")))throw new ServiceException("搜索地点与目录标识重复。");
            all.Add(p);
        }
        return all;
    }
}
