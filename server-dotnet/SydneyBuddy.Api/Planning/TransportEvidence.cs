using System.Globalization;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public sealed class TransportEvidence
{
    readonly Dictionary<string,JsonObject> values=[];
    public static string Key(JsonObject a,JsonObject b,string date,int minute)=>WalkingEvidence.Key(a,b)+"|"+date+"|"+minute;
    public TransportEvidence(JsonArray? input,bool allowHistorical=false) {
        foreach(var value in input?.OfType<JsonObject>()??[]) {
            if(values.Count>=120)throw new ServiceException("Too many transport records.");
            var from=value.Object("from")??throw new ServiceException("Transport data is missing its origin.");var to=value.Object("to")??throw new ServiceException("Transport data is missing its destination.");
            var leg=value.Object("leg")??throw new ServiceException("Transport data is missing route legs.");
            if(leg.Str("mode")!="transit"||leg.Array("steps") is not {} steps||steps.Count==0)throw new ServiceException("Invalid public transport data.");
            if(!DateTimeOffset.TryParse(leg.Str("departureTime"),out var depart)||!DateTimeOffset.TryParse(leg.Str("arrivalTime"),out var arrive)||arrive<depart||leg.Int("minutes")!=(int)Math.Ceiling((arrive-depart).TotalMinutes))throw new ServiceException("Public transport times are inconsistent.");
            if(!DateTimeOffset.TryParse(leg.Str("fetchedAt"),out var at)||(!allowHistorical&&at<DateTimeOffset.UtcNow.AddHours(-6))||at>DateTimeOffset.UtcNow.AddMinutes(5))throw new ServiceException("Public transport data has expired. Please search again.");
            if(leg.Number("walkingMeters",-1)<0||leg.Number("meters",-1)<leg.Number("walkingMeters"))throw new ServiceException("Invalid walking connection data for public transport.");
            var key=Key(from,to,value.Str("date"),value.Int("minute"));if(!values.TryAdd(key,leg.DeepClone().AsObject()))throw new ServiceException("Duplicate public transport legs.");
        }
    }
    public JsonObject? Get(JsonObject a,JsonObject b,string date,int minute)=>values.TryGetValue(Key(a,b,date,minute),out var leg)?leg.DeepClone().AsObject():null;
}
