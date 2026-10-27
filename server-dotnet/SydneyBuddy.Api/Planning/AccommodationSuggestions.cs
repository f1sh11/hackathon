using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public static class AccommodationSuggestions
{
    public static JsonArray Select(JsonObject profile,IReadOnlyList<JsonObject> stays,JsonArray? requested,JsonArray days)
    {
        if(profile.Str("accommodationStatus")=="booked")return new();
        var ids=requested?.Select(x=>x!.ToString()).ToHashSet()??[];
        var activities=days.OfType<JsonObject>().SelectMany(d=>d.Array("stops")!.OfType<JsonObject>()).Where(p=>!RouteLogistics.IsTask(p)).ToArray();
        var center=activities.Length==0?profile.Object("start")!:new JsonObject{["lat"]=activities.Average(p=>p.Number("lat")),["lng"]=activities.Average(p=>p.Number("lng"))};
        var pool=ids.Count>0?stays.Where(s=>ids.Contains(s.Str("id"))):stays;
        return new JsonArray(pool.OrderBy(s=>RouteService.Distance(center,s)).Take(ids.Count>0?5:3).Select(s=>{
            var item=(JsonObject)s.DeepClone();item["bookingStatus"]="unbooked";item["availabilityVerified"]=false;item["rateVerified"]=false;
            item["suggestionBasis"]=ids.Count>0?"Agent-selected supplied candidates, ordered by approximate activity-area distance":"Supplied lodging candidates near the planned activity area; approximate straight-line ranking, not verified road distance";
            return (JsonNode)item;
        }).ToArray());
    }
}
