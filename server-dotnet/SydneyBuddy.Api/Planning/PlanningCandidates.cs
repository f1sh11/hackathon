using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Request-scoped search results. Never mutate the shared catalog or present unknown facts as verified.
public static class PlanningCandidates
{
    public static IReadOnlyList<JsonObject> Merge(IReadOnlyList<JsonObject> catalog,JsonArray? input)
    {
        var all=catalog.Select(p=>p.DeepClone().AsObject()).ToList();
        if(input is null)return all;
        if(input.Count>60)throw new ServiceException("Too many search candidates. Reduce the planning area.");
        var seen=new HashSet<string>();
        foreach(var node in input) {
            if(node is not JsonObject value||!seen.Add(value.Str("id")))throw new ServiceException("Search candidates are invalid or duplicated.");
            var p=PoiService.Import(value);
            p["activityType"]=PoiService.ActivityKind(p)=="meal"?"meal":"attraction";
            var category=value.Str("category");
            if(category is "自然" or "文化" or "艺术" or "购物" or "城市" or "风景")p["category"]=category;
            if(p.Str("category")=="風景")p["category"]="风景";
            if(p.Str("category")=="店家")p["category"]="购物";
            p["tags"]=new JsonArray(p.Str("category"));p["indoorKnown"]=false;
            p["openMinute"]=0;p["closeMinute"]=1440;p["hoursVerified"]=false;p["costKnown"]=false;
            p["cost"]=p.Str("activityType")=="meal"?30:0;
            p["note"]="Actual search result. Opening hours, ticket/menu prices and indoor status are unverified."+(p.Str("activityType")=="meal"?"A$30 per person is reserved for meals; this is not a menu quote.":"Unknown admission fees are excluded; this does not mean free entry.");
            if(all.Any(x=>x.Str("id")==p.Str("id")))throw new ServiceException("A searched place duplicates a catalog ID.");
            all.Add(p);
        }
        return all;
    }
}
