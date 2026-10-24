using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

public static class WalkingTimeUpdate
{
    public static JsonObject Apply(JsonObject original,int dayIndex,JsonArray legs,IReadOnlyList<JsonObject> catalog)
    {
        var result=(JsonObject)original.DeepClone();
        if(result.Object("schedule") is not null)throw new ServiceException("此行程已有手动时间安排，暂不自动覆盖。请在编辑行程中参考实际步行用时调整。");
        var days=result.Array("days")!;
        if(dayIndex<0 || dayIndex>=days.Count)throw new ServiceException("更新日期无效。");
        var day=days[dayIndex]!.AsObject();var stops=day.Array("stops")!;var profile=result.Object("profile")!;
        if(legs.Count!=stops.Count+1 || stops.Count==0)throw new ServiceException("步行路线已变化，请重新查询。");
        var normalized=new JsonArray();var clock=profile.Int("startMinute");var steps=0;
        static string Time(int n)=>$"{n/60:00}:{n%60:00}";
        for(var i=0;i<legs.Count;i++) {
            var leg=legs[i] as JsonObject ?? throw new ServiceException("步行路段无效。");
            var meters=leg.Number("meters",double.NaN);var seconds=leg.Number("seconds",double.NaN);
            if(!double.IsFinite(meters)||!double.IsFinite(seconds)||meters<0||meters>100000||seconds<0||seconds>86400)throw new ServiceException("步行用时或距离无效。");
            var minutes=(int)Math.Ceiling(seconds/60);
            normalized.Add(new JsonObject{["meters"]=(int)Math.Ceiling(meters),["minutes"]=minutes,["polyline"]="",["source"]="Apple Maps / MapKit 步行查询"});
            steps+=(int)Math.Ceiling(meters/0.75);clock+=minutes+10;
            if(i==stops.Count)break;
            var stop=stops[i]!.AsObject();var point=catalog.FirstOrDefault(p=>p.Str("id")==stop.Str("id"));
            if(point is not null) {
                var windows=RouteScheduler.OpeningWindows(point,day.Str("date"),out _);
                var feasible=windows.Select(w=>(Start:Math.Max(clock,w.Open),w.Close)).Where(w=>w.Start+stop.Int("visitMinutes")<=w.Close).OrderBy(w=>w.Start).ToArray();
                if(feasible.Length==0)throw new ServiceException(stop.Str("name")+"按实际步行时间无法在开放时段内游览，请先调整安排。");
                clock=feasible[0].Start;
            }else if(stop.Bool("hoursVerified")) {
                // Unknown imported hours cannot be revalidated from the saved display labels.
                throw new ServiceException("有地点的开放时段无法重新核实，请手动调整当天安排。");
            }
            stop["arrival"]=Time(clock);clock+=stop.Int("visitMinutes");stop["departure"]=Time(clock);clock+=15;
            steps+=stop.Int("visitMinutes")*12;stop["steps"]=(int)Math.Ceiling(meters/0.75)+stop.Int("visitMinutes")*12;
        }
        if(clock>profile.Int("endMinute") || steps>profile.Int("maxSteps"))throw new ServiceException("实际步行路线超过当天的返程时间或步数上限，请先减少地点或调整限制。原行程已保留。");
        day["legs"]=normalized;day["estimatedSteps"]=steps;day["finishTime"]=Time(clock);
        day.Array("warnings")!.Add("已按 Apple 实际步行查询更新；包含每站10分钟衔接、15分钟休息。开放时间和排队仍需核实。");
        var snapshot=(JsonObject)original.DeepClone();snapshot.Remove("previousAdjustment");result["previousAdjustment"]=snapshot;
        TripContractValidator.Validate(result);return result;
    }
}
