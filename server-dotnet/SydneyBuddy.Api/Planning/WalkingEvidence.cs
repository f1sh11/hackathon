using System.Globalization;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Client MapKit observations are bounded, directional and operation scoped. They are not server-attested.
public sealed class WalkingEvidence
{
    readonly Dictionary<string,JsonObject> legs=[];
    public static string Key(JsonObject a,JsonObject b)=>string.Join(",",new[]{a.Number("lat"),a.Number("lng"),b.Number("lat"),b.Number("lng")}.Select(n=>n.ToString("F6",CultureInfo.InvariantCulture)));
    public WalkingEvidence(JsonArray input) {
        if(input.Count>1200)throw new ServiceException("Too many walking legs. Reduce the planning area.");
        foreach(var node in input) {
            var x=node as JsonObject ?? throw new ServiceException("Invalid walking evidence format.");
            var a=x.Object("from");var b=x.Object("to");
            if(a is null||b is null)throw new ServiceException("Walking leg coordinates are missing.");
            foreach(var p in new[]{a,b})if(!double.IsFinite(p.Number("lat",double.NaN))||!double.IsFinite(p.Number("lng",double.NaN))||p.Number("lat") is < -34.2 or > -33.5||p.Number("lng") is <150.5 or >151.5)throw new ServiceException("Walking leg coordinates are outside the supported area.");
            if(!DateTimeOffset.TryParse(x.Str("fetchedAt"),out var at)||at>DateTimeOffset.UtcNow.AddMinutes(5)||at<DateTimeOffset.UtcNow.AddHours(-6))throw new ServiceException("Walking data has expired. Please search again.");
            var key=Key(a,b);if(legs.ContainsKey(key))throw new ServiceException("Duplicate walking leg.");
            if(x.Bool("unreachable")){legs[key]=new(){["unreachable"]=true};continue;}
            var meters=x.Number("meters",double.NaN);var seconds=x.Number("seconds",double.NaN);
            if(!double.IsFinite(meters)||!double.IsFinite(seconds)||meters<0||meters>100000||seconds<0||seconds>86400||meters>0&&seconds==0)throw new ServiceException("Invalid walking time or distance.");
            legs[key]=new(){["meters"]=(int)Math.Ceiling(meters),["minutes"]=(int)Math.Ceiling(seconds/60),["polyline"]="",["source"]="Apple Maps / MapKit walking directions",["fetchedAt"]=x.Str("fetchedAt")};
        }
    }
    public JsonObject Get(JsonObject a,JsonObject b) {
        if(RouteService.Distance(a,b)<1)return new(){["meters"]=0,["minutes"]=0,["polyline"]="",["source"]="Apple Maps / MapKit same location"};
        if(!legs.TryGetValue(Key(a,b),out var value))throw new ServiceException("Actual walking legs are missing. Search again before planning.");
        return (JsonObject)value.DeepClone();
    }
}
