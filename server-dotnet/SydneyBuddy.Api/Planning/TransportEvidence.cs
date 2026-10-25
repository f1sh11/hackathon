using System.Globalization;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public sealed class TransportEvidence
{
    readonly Dictionary<string,JsonObject> values=[];
    public static string Key(JsonObject a,JsonObject b,string date,int minute)=>WalkingEvidence.Key(a,b)+"|"+date+"|"+minute;
    public TransportEvidence(JsonArray? input,bool allowHistorical=false) {
        foreach(var value in input?.OfType<JsonObject>()??[]) {
            if(values.Count>=120)throw new ServiceException("交通证据过多。");
            var from=value.Object("from")??throw new ServiceException("交通资料缺少起点。");var to=value.Object("to")??throw new ServiceException("交通资料缺少终点。");
            var leg=value.Object("leg")??throw new ServiceException("交通资料缺少路段。");
            if(leg.Str("mode")!="transit"||leg.Array("steps") is not {} steps||steps.Count==0)throw new ServiceException("公共交通资料无效。");
            if(!DateTimeOffset.TryParse(leg.Str("departureTime"),out var depart)||!DateTimeOffset.TryParse(leg.Str("arrivalTime"),out var arrive)||arrive<depart||leg.Int("minutes")!=(int)Math.Ceiling((arrive-depart).TotalMinutes))throw new ServiceException("公共交通时间不一致。");
            if(!DateTimeOffset.TryParse(leg.Str("fetchedAt"),out var at)||(!allowHistorical&&at<DateTimeOffset.UtcNow.AddHours(-6))||at>DateTimeOffset.UtcNow.AddMinutes(5))throw new ServiceException("公共交通数据已过期，请重新查询。");
            if(leg.Number("walkingMeters",-1)<0||leg.Number("meters",-1)<leg.Number("walkingMeters"))throw new ServiceException("公共交通步行接驳资料无效。");
            var key=Key(from,to,value.Str("date"),value.Int("minute"));if(!values.TryAdd(key,leg.DeepClone().AsObject()))throw new ServiceException("公共交通路段重复。");
        }
    }
    public JsonObject? Get(JsonObject a,JsonObject b,string date,int minute)=>values.TryGetValue(Key(a,b,date,minute),out var leg)?leg.DeepClone().AsObject():null;
}
