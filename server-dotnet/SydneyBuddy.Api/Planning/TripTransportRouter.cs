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
        if(!Configured)throw new ServiceException("公共交通尚未接通。请配置 TfNSW 后重试，或明确选择只步行。");
        if(!walk.Bool("unreachable")&&walk.Int("minutes")<15)return walk;
        var date=DateOnly.ParseExact(profile.Str("date"),"yyyy-MM-dd");
        var local=date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute);
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");var departure=new DateTimeOffset(local,zone.GetUtcOffset(local));
        var key=WalkingEvidence.Key(from,to)+"|"+departure.ToString("O");
        if(context.TransitCache.TryGetValue(key,out var cached))return (JsonObject)cached.DeepClone();
        if(context.TransitQueries>=40)throw new ServiceException("本次交通比较已达查询上限，请缩小每天的活动范围后重试。");
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
            var result=new JsonObject{["meters"]=(int)Math.Ceiling(steps.Sum(s=>s.Meters)),["walkingMeters"]=(int)Math.Ceiling(walking),["minutes"]=(int)Math.Ceiling(seconds/60),["source"]="Transport for NSW / Trip Planner",["mode"]="transit",["polyline"]="",["departureTime"]=departure.ToString("O"),["arrivalTime"]=steps[^1].ArrivalTime.ToString("O"),["fetchedAt"]=DateTimeOffset.UtcNow.ToString("O"),["steps"]=JsonSerializer.SerializeToNode(steps,JsonExtensions.Options),["fareKnown"]=false,["transfers"]=transfers,["reason"]="比较了步行、等车、接驳与换乘；公交票价未知，未包含在预算。"};
            context.TransitCache[key]=(JsonObject)result.DeepClone();return result;
        }catch(ServiceException ex) when(!walk.Bool("unreachable")&&(ex.Message=="NSW 未找到大众运输班次，可改选步行。"||ex.Message=="NSW 此区间或日期没有可用班次。")) {
            // A verified absence of transit is different from an unavailable/broken provider.
            context.TransitCache[key]=walk.DeepClone().AsObject();return walk;
        }catch(ServiceException ex) {
            // A failed provider is never silently converted to a successful walking-only recommendation.
            var reason=ex.Message.StartsWith("NSW 班次时间不连续")?"班次时间不连续":ex.Message.StartsWith("NSW 回传尚未支持")?"交通方式暂不支持":ex.Message.StartsWith("此班次需要预约")?"该班次需要预约":"服务暂不可用或回传资料不完整";
            throw new ServiceException("公共交通班次未能核实（"+reason+"），请稍后重试，或明确选择只步行。原行程保持不变。");
        }
    }
}
