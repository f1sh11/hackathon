using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public static class WalkingTimeUpdate
{
    public static JsonObject Apply(JsonObject original,int dayIndex,JsonArray legs,IReadOnlyList<JsonObject> catalog)
    {
        var result=(JsonObject)original.DeepClone();
        if(result.Object("schedule") is not null)throw new ServiceException("This trip has manually scheduled times. Use the actual walking times to adjust it in the trip editor.");
        var days=result.Array("days")!;
        if(dayIndex<0 || dayIndex>=days.Count)throw new ServiceException("Invalid update date.");
        var day=days[dayIndex]!.AsObject();var stops=day.Array("stops")!;var profile=result.Object("profile")!;
        if(legs.Count!=stops.Count+1 || stops.Count==0)throw new ServiceException("The walking route has changed. Please search again.");
        var normalized=new JsonArray();var clock=day.Int("planningStartMinute",profile.Int("startMinute"));var steps=0;
        static string Time(int n)=>$"{n/60:00}:{n%60:00}";
        for(var i=0;i<legs.Count;i++) {
            var leg=legs[i] as JsonObject ?? throw new ServiceException("Invalid walking leg.");
            var meters=leg.Number("meters",double.NaN);var seconds=leg.Number("seconds",double.NaN);
            if(!double.IsFinite(meters)||!double.IsFinite(seconds)||meters<0||meters>100000||seconds<0||seconds>86400)throw new ServiceException("Invalid walking time or distance.");
            var minutes=(int)Math.Ceiling(seconds/60);
            normalized.Add(new JsonObject{["meters"]=(int)Math.Ceiling(meters),["minutes"]=minutes,["polyline"]="",["source"]="Apple Maps / MapKit walking directions"});
            steps+=(int)Math.Ceiling(meters/0.75);clock+=minutes+10;
            if(i==stops.Count)break;
            var stop=stops[i]!.AsObject();var point=catalog.FirstOrDefault(p=>p.Str("id")==stop.Str("id"));
            if(point is not null) {
                var windows=RouteScheduler.OpeningWindows(point,day.Str("date"),out _);
                var feasible=windows.Select(w=>(Start:Math.Max(clock,w.Open),w.Close)).Where(w=>w.Start+stop.Int("visitMinutes")<=w.Close).OrderBy(w=>w.Start).ToArray();
                if(feasible.Length==0)throw new ServiceException(stop.Str("name")+": actual walking times leave too little time to visit during opening hours. Please adjust your schedule.");
                clock=feasible[0].Start;
            }else if(stop.Bool("hoursVerified")) {
                // Unknown imported hours cannot be revalidated from the saved display labels.
                throw new ServiceException("Some opening hours could not be verified again. Please adjust that day manually.");
            }
            stop["arrival"]=Time(clock);clock+=stop.Int("visitMinutes");stop["departure"]=Time(clock);clock+=15;
            steps+=stop.Int("visitMinutes")*12;stop["steps"]=(int)Math.Ceiling(meters/0.75)+stop.Int("visitMinutes")*12;
        }
        if(clock>profile.Int("endMinute") || steps>profile.Int("maxSteps"))throw new ServiceException("The actual walking route exceeds the return time or step limit. Remove some stops or adjust your limits. Your original trip has been kept.");
        day["planningStartMinute"]=day.Int("planningStartMinute",profile.Int("startMinute"));day["legs"]=normalized;day["estimatedSteps"]=steps;day["finishTime"]=Time(clock);
        day.Array("warnings")!.Add("Updated using actual Apple walking directions, with 10 minutes for connections and 15 minutes of rest at each stop. Opening hours and queues still need checking.");
        var snapshot=(JsonObject)original.DeepClone();snapshot.Remove("previousAdjustment");result["previousAdjustment"]=snapshot;
        TripContractValidator.Validate(result);return result;
    }
}
