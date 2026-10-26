using System.Text.Json;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Timetable calls only for bounded shortlisted routes; waits and access walking remain part of the trip.
public sealed class TripTransportRouter(NswJourneyProvider nsw,SecretStore secrets)
{
    public bool Configured=>!string.IsNullOrWhiteSpace(secrets.Read()["TFNSW_API_KEY"]);
    public async Task<JsonObject> ChooseAsync(JsonObject from,JsonObject to,JsonObject walk,JsonObject profile,int minute,PlanningContext context,CancellationToken ct)
    {
        var policy=profile.Str("transportPreference","balanced");
        if(policy=="walk")return walk;
        if(!Configured)throw new ServiceException("Public transport is not connected. Configure TfNSW and retry, or select walking only.");
        if(!walk.Bool("unreachable")&&walk.Int("minutes")<15)return walk;
        var date=DateOnly.ParseExact(profile.Str("date"),"yyyy-MM-dd");
        var local=date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute);
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");var departure=new DateTimeOffset(local,zone.GetUtcOffset(local));
        var key=WalkingEvidence.Key(from,to)+"|"+departure.ToString("O");
        if(context.TransitCache.TryGetValue(key,out var cached))return (JsonObject)cached.DeepClone();
        if(context.TransitQueries>=40)throw new ServiceException("This transport comparison reached its query limit. Reduce each day's activity area and try again.");
        context.TransitQueries++;
        try {
            var steps=await nsw.RouteAsync(new(from.Str("name"),from.Number("lat"),from.Number("lng"),0),new(to.Str("name"),to.Number("lat"),to.Number("lng"),0),"transit",departure,ct);
            if(!steps.Any(s=>s.Mode!="walk"))return walk;
            var seconds=(steps[^1].ArrivalTime-departure).TotalSeconds;
            var walking=steps.Where(s=>s.Mode=="walk").Sum(s=>s.Meters);
            var transfers=Math.Max(0,steps.Count(s=>s.Mode!="walk")-1);
            var walkSeconds=walk.Number("minutes")*60;
            var worthwhile=walk.Bool("unreachable")||seconds+transfers*240<walkSeconds-120||
                ((policy=="lessWalking"||profile.Int("maxSteps")<=6000)&&walking+300<walk.Number("meters")&&seconds<walkSeconds+600);
            if(!worthwhile){context.TransitCache[key]=(JsonObject)walk.DeepClone();return walk;}
            var result=new JsonObject{["meters"]=(int)Math.Ceiling(steps.Sum(s=>s.Meters)),["walkingMeters"]=(int)Math.Ceiling(walking),["minutes"]=(int)Math.Ceiling(seconds/60),["source"]="Transport for NSW / Trip Planner",["mode"]="transit",["polyline"]="",["departureTime"]=departure.ToString("O"),["arrivalTime"]=steps[^1].ArrivalTime.ToString("O"),["fetchedAt"]=DateTimeOffset.UtcNow.ToString("O"),["steps"]=JsonSerializer.SerializeToNode(steps,JsonExtensions.Options),["fareKnown"]=false,["transfers"]=transfers,["reason"]="Walking, waiting, connections and transfers were compared. Public transport fares are unknown and excluded from the budget."};
            context.TransitCache[key]=(JsonObject)result.DeepClone();return result;
        }catch(ServiceException ex) when(!walk.Bool("unreachable")&&(ex.Message=="NSW found no public transport services. You can switch to walking."||ex.Message=="No NSW services are available for this journey or date.")) {
            // A verified absence of transit is different from an unavailable/broken provider.
            context.TransitCache[key]=walk.DeepClone().AsObject();return walk;
        }catch(ServiceException ex) {
            // A failed provider is never silently converted to a successful walking-only recommendation.
            var reason=ex.Message.StartsWith("NSW service times are inconsistent")?"inconsistent service times":ex.Message.StartsWith("NSW returned an unsupported")?"unsupported transport mode":ex.Message.StartsWith("This service requires a booking")?"this service requires a booking":"the service is unavailable or returned incomplete data";
            throw new ServiceException("Public transport services could not be verified ("+reason+"). Try again later or select walking only. Your original trip is unchanged.");
        }
    }
}
