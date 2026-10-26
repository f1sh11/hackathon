using System.Globalization;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Client MapKit observations are bounded, directional and operation scoped. They are not server-attested.
public sealed class WalkingEvidence
{
    readonly Dictionary<string,JsonObject> legs=[];
    public static string Key(JsonObject a,JsonObject b)=>string.Join(",",new[]{a.Number("lat"),a.Number("lng"),b.Number("lat"),b.Number("lng")}.Select(n=>n.ToString("F6",CultureInfo.InvariantCulture)));
    public WalkingEvidence(JsonArray input) {
        if(input.Count>10000)throw new ServiceException("步行路段过多，请缩小规划范围。");
        foreach(var node in input) {
            var x=node as JsonObject ?? throw new ServiceException("步行证据格式无效。");
            var a=x.Object("from");var b=x.Object("to");
            if(a is null||b is null)throw new ServiceException("步行路段缺少坐标。");
            foreach(var p in new[]{a,b})if(!double.IsFinite(p.Number("lat",double.NaN))||!double.IsFinite(p.Number("lng",double.NaN))||p.Number("lat") is < -37.5 or > -28.0||p.Number("lng") is <141.0 or >154.0)throw new ServiceException("步行路段坐标超出支持范围。");
            if(!DateTimeOffset.TryParse(x.Str("fetchedAt"),out var at)||at>DateTimeOffset.UtcNow.AddMinutes(5)||at<DateTimeOffset.UtcNow.AddHours(-6))throw new ServiceException("步行数据已过期，请重新查询。");
            var key=Key(a,b);if(legs.ContainsKey(key))throw new ServiceException("步行路段重复。");
            if(x.Bool("unreachable")){legs[key]=new(){["unreachable"]=true};continue;}
            var meters=x.Number("meters",double.NaN);var seconds=x.Number("seconds",double.NaN);
            if(!double.IsFinite(meters)||!double.IsFinite(seconds)||meters<0||meters>100000||seconds<0||seconds>86400||meters>0&&seconds==0)throw new ServiceException("步行时间或距离无效。");
            legs[key]=new(){["meters"]=(int)Math.Ceiling(meters),["minutes"]=(int)Math.Ceiling(seconds/60),["polyline"]="",["source"]="Apple Maps / MapKit 步行查询",["fetchedAt"]=x.Str("fetchedAt")};
        }
    }
    public JsonObject Get(JsonObject a,JsonObject b) {
        if(RouteService.Distance(a,b)<1)return new(){["meters"]=0,["minutes"]=0,["polyline"]="",["source"]="Apple Maps / MapKit 同一位置"};
        if(!legs.TryGetValue(Key(a,b),out var value))throw new ServiceException("缺少真实步行路段，请重新查询后再规划。");
        return (JsonObject)value.DeepClone();
    }
}
