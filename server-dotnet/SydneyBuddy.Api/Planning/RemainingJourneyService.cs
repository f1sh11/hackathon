using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public sealed class RemainingJourneyService(ScheduleEditorService editor,PlaceCatalog catalog,TransportScheduleService transport,PlanningRunJournal? journal=null)
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
        if(TravelAdjustmentIntent.Lighter(request.Str("request")))limit=Math.Min(limit,8000);
        target["maxSteps"]=limit;
        var required=draft.Object("profile")!.Array("mustVisit")!.Select(n=>n!.GetValue<string>()).ToHashSet();
        for(var attempt=0;attempt<10;attempt++) {
            var input=new JsonObject{["trip"]=draft.DeepClone(),["days"]=draft.Object("schedule")!["days"]!.DeepClone(),["settings"]=draft.Object("schedule")!["settings"]!.DeepClone(),["walkingRoutes"]=request["walkingRoutes"]!.DeepClone(),["transportDays"]=new JsonArray(index)};
            journal?.Record("RemainingPlanner","deterministic","recompute_remaining","running",attempt:attempt,target:"RouteEvaluator");
            var updated=await transport.BuildAsync(input,ct);
            var issues=updated.Object("schedule")!.Array("issues")!.OfType<JsonObject>().Where(i=>i.Bool("blocking")&&(i.Int("day")==index||i.Int("day")==-1)).ToArray();
            journal?.Record("RouteEvaluator","deterministic","validate_remaining",issues.Length==0?"approved":"needs_revision",new(){["blocking_count"]=issues.Length},attempt,"RemainingPlanner");
            if(issues.Length==0) {
                var result=original.DeepClone().AsObject();result["profile"]=draft["profile"]!.DeepClone();if(draft["planningCandidates"] is {} candidates)result["planningCandidates"]=candidates.DeepClone();result.Array("days")![index]=updated.Array("days")![index]!.DeepClone();
                result["schedule"]=draft["schedule"]!.DeepClone();result.Object("schedule")!.Array("days")![index]=updated.Object("schedule")!.Array("days")![index]!.DeepClone();
                result["walkingRoutes"]=request["walkingRoutes"]!.DeepClone();result["transportRoutes"]=updated["transportRoutes"]!.DeepClone();
                result["explanation"]="已保留已完成与跳过的活动，按"+(request["currentMinute"] is null?"该天计划的出发时间和位置":"当前时间和位置")+"重新比较步行、等车、接驳与公共交通；其他日期保持不变。票价未知。";
                return result;
            }
            var list=draft.Object("schedule")!.Array("days")![index]!.Array("activities")!;
            var optional=list.OfType<JsonObject>().LastOrDefault(a=>!a.Bool("locked")&&a["fixedStart"] is null&&!required.Contains(a.Str("placeId"))&&a.Str("kind")=="attraction");
            if(optional is null)throw new ServiceException("当前班次、剩余时间或步数无法满足固定安排，请调整需求。原行程已保留。");
            journal?.Record("RemainingPlanner","deterministic","remove_optional_stop","success",new(){["constraints_relaxed"]=false},attempt,"RouteEvaluator");
            list.Remove(optional);
        }
        throw new ServiceException("剩余安排暂时无法核实，原行程已保留。");
    }
}
