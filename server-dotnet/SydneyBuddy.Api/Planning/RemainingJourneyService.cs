using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public sealed class RemainingJourneyService(ScheduleEditorService editor,PlaceCatalog catalog,TransportScheduleService transport)
{
    public async Task<JsonObject> BuildAsync(JsonObject original,JsonObject request,CancellationToken ct) {
        var policy=request.Object("constraints")?.Str("transportPreference",original.Object("profile")!.Str("transportPreference","walk"))??original.Object("profile")!.Str("transportPreference","walk");
        if(policy=="walk") {var walking=original.DeepClone().AsObject();walking["transportRoutes"]=new JsonArray();return RemainingJourney.Build(walking,request,editor,catalog,ct);}
        var seed=original.DeepClone().AsObject();
        var selectedDate=original.Array("days")![request.Int("day")]!.Str("date");
        seed["transportRoutes"]=new JsonArray((original.Array("transportRoutes")?.OfType<JsonObject>()??[]).Where(x=>x.Str("date")!=selectedDate).Select(x=>x.DeepClone()).ToArray());
        var draft=RemainingJourney.Build(seed,request,editor,catalog,ct,transportPreflight:true);
        var index=request.Int("day");var target=draft.Object("schedule")!.Array("days")![index]!.AsObject();
        var limit=request.Object("constraints")?.Int("maxSteps",original.Object("profile")!.Int("maxSteps"))??original.Object("profile")!.Int("maxSteps");
        if(TravelAdjustmentIntent.Lighter(request.Str("request")))limit=Math.Min(limit,7000);
        target["maxSteps"]=limit;
        var required=draft.Object("profile")!.Array("mustVisit")!.Select(n=>n!.GetValue<string>()).ToHashSet();
        for(var attempt=0;attempt<10;attempt++) {
            var input=new JsonObject{["trip"]=draft.DeepClone(),["days"]=draft.Object("schedule")!["days"]!.DeepClone(),["settings"]=draft.Object("schedule")!["settings"]!.DeepClone(),["walkingRoutes"]=request["walkingRoutes"]!.DeepClone(),["transportDays"]=new JsonArray(index)};
            var updated=await transport.BuildAsync(input,ct);
            var issues=updated.Object("schedule")!.Array("issues")!.OfType<JsonObject>().Where(i=>i.Bool("blocking")&&(i.Int("day")==index||i.Int("day")==-1)).ToArray();
            if(issues.Length==0) {
                var result=original.DeepClone().AsObject();result["profile"]=draft["profile"]!.DeepClone();if(draft["planningCandidates"] is {} candidates)result["planningCandidates"]=candidates.DeepClone();result.Array("days")![index]=updated.Array("days")![index]!.DeepClone();
                result["schedule"]=draft["schedule"]!.DeepClone();result.Object("schedule")!.Array("days")![index]=updated.Object("schedule")!.Array("days")![index]!.DeepClone();
                result["walkingRoutes"]=request["walkingRoutes"]!.DeepClone();result["transportRoutes"]=updated["transportRoutes"]!.DeepClone();
                result["explanation"]="Kept completed and skipped activities and compared routes using "+(request["currentMinute"] is null?"the day's planned departure time and location":"the current time and location")+", including walking, waiting, connections and public transport. Other dates are unchanged. Fares are unknown.";
                return result;
            }
            var list=draft.Object("schedule")!.Array("days")![index]!.Array("activities")!;
            var optional=list.OfType<JsonObject>().LastOrDefault(a=>!a.Bool("locked")&&a["fixedStart"] is null&&!required.Contains(a.Str("placeId"))&&a.Str("kind")=="attraction");
            if(optional is null)throw new ServiceException("Current services, remaining time or steps cannot meet the fixed schedule. Adjust your requirements. Your original trip is kept.");
            list.Remove(optional);
        }
        throw new ServiceException("The remaining schedule could not be verified. Your original trip is kept.");
    }
}
