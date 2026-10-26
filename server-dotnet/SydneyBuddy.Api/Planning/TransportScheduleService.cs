using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Re-query timetable at the actual departure after every edit, then run the same schedule constraints.
public sealed class TransportScheduleService(ScheduleEditorService editor,TripTransportRouter router)
{
    public async Task<JsonObject> BuildAsync(JsonObject request,CancellationToken ct) {
        var trip=request.Object("trip")??throw new ServiceException("Trip is missing.");
        var profile=trip.Object("profile")!;
        if(profile.Str("transportPreference","walk")=="walk")return editor.Build(request);
        if(!router.Configured)throw new ServiceException("Public transport is not configured. Your draft has been kept.");
        var body=request.DeepClone().AsObject();
        var selected=request.Array("transportDays")?.Select(n=>n!.GetValue<int>()).ToHashSet();
        var untouchedDates=trip.Array("days")?.OfType<JsonObject>().Select((day,index)=>(day,index)).Where(x=>selected is not null&&!selected.Contains(x.index)).Select(x=>x.day.Str("date")).ToHashSet()??[];
        var evidence=new JsonArray((trip.Array("transportRoutes")?.OfType<JsonObject>()??[]).Where(x=>untouchedDates.Contains(x.Str("date"))).Select(x=>x.DeepClone()).ToArray());
        body["transportRoutes"]=evidence.DeepClone();
        var working=editor.Build(body,allowHistoricalTransit:true);
        var context=new PlanningContext("demo",new Dictionary<string,string>());
        var walks=new WalkingEvidence(body.Array("walkingRoutes")??trip.Array("walkingRoutes")??throw new ServiceException("Apple walking data is missing."));
        for(var d=0;d<working.Array("days")!.Count;d++) {
            if(selected is not null&&!selected.Contains(d))continue;
            for(var i=0;i<=working.Array("days")![d]!.Array("stops")!.Count;i++) {
                ct.ThrowIfCancellationRequested();
                var day=working.Array("days")![d]!.AsObject();var stops=day.Array("stops")!.OfType<JsonObject>().ToArray();var activities=working.Object("schedule")!.Array("days")![d]!.Array("activities")!;
                var from=i==0?day.Object("start")!:stops[i-1];var to=i==stops.Length?day.Object("end")!:stops[i];
                var minute=i==0?day.Int("planningStartMinute",profile.Int("startMinute")):Minute(activities[i-1]!.Str("departure"))+15;
                var daily=profile.DeepClone().AsObject();daily["date"]=day.Str("date");daily["maxSteps"]=working.Object("schedule")!.Array("days")![d]!.Int("maxSteps",profile.Int("maxSteps"));
                var leg=await router.ChooseAsync(from,to,walks.Get(from,to),daily,minute,context,ct);
                if(leg.Str("mode")=="transit")evidence.Add(new JsonObject{["from"]=from.DeepClone(),["to"]=to.DeepClone(),["date"]=day.Str("date"),["minute"]=minute,["leg"]=leg});
                body["transportRoutes"]=evidence.DeepClone();working=editor.Build(body,allowHistoricalTransit:true);
            }
        }
        working["transportRoutes"]=evidence.DeepClone();return working;
    }
    static int Minute(string text){var p=text.Split(':');return int.Parse(p[0])*60+int.Parse(p[1]);}
}
