using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>行程集合的 REST 入口；trip endpoints validate save contracts before repository calls.</summary>
public static class TripEndpoints
{
    public static void MapTripEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/trips", async (TripRepository trips, CancellationToken ct) => Results.Json(await trips.ListAsync(ct)));
        routes.MapPost("/trips", async (HttpContext context, TripRepository trips, ScheduleEditorService editor, PlaceCatalog catalog,RemainingJourneyService remainingService) =>
        {
            var input = await RequestJson.ReadAsync(context);
            if (input["trip"] is not JsonObject trip) return Results.Json(new { error = "请求格式不合法" }, statusCode: 400);
            trip.Remove("previousAdjustment");
            if (trip["id"] is not null && (trip["id"] is not JsonValue id || !id.TryGetValue<string>(out _)))
                return Results.Json(new { error = "行程标识必须是字符串" }, statusCode: 400);
            if(input.Object("remainingRequest") is {} remaining) {
                var existing=await trips.FindAsync(trip.Str("id"),context.RequestAborted)??throw new ServiceException("行程不存在。");
                if(existing.Int("revision")!=input.Int("expectedRevision"))throw new RevisionConflictException();
                var updated=await remainingService.BuildAsync(existing,remaining,context.RequestAborted);var snapshot=(JsonObject)existing.DeepClone();snapshot.Remove("previousAdjustment");updated["previousAdjustment"]=snapshot;
                return Results.Json(await trips.SaveAsync(updated,input.Int("expectedRevision"),context.RequestAborted));
            }
            if(input["walkingTimeDay"] is not null) {
                var existing=await trips.FindAsync(trip.Str("id"),context.RequestAborted) ?? throw new ServiceException("行程不存在。");
                if(existing.Int("revision")!=input.Int("expectedRevision"))throw new RevisionConflictException();
                if(input["walkingTimeDay"] is not JsonValue day || !day.TryGetValue<int>(out var index) || input["walkingLegs"] is not JsonArray legs)throw new ServiceException("步行路线资料无效。");
                return Results.Json(await trips.SaveAsync(WalkingTimeUpdate.Apply(existing,index,legs,catalog.Get()),input.Int("expectedRevision"),context.RequestAborted));
            }
            if(input.Bool("renameOnly")) {
                var existing=await trips.FindAsync(trip.Str("id"),context.RequestAborted) ?? throw new ServiceException("行程不存在。");
                if(existing.Int("revision")!=input.Int("expectedRevision"))throw new RevisionConflictException();
                var title=trip.Str("title").Trim();if(title.Length is 0 or >40)throw new ServiceException("旅行名称需为 1–40 个字符。");
                existing["title"]=title;return Results.Json(await trips.SaveAsync(existing,input.Int("expectedRevision"),context.RequestAborted));
            }
            if(input.Bool("undoAdjustment")) {
                var current=await trips.FindAsync(trip.Str("id"),context.RequestAborted) ?? throw new ServiceException("行程不存在。");
                if(current.Int("revision")!=input.Int("expectedRevision"))throw new RevisionConflictException();
                var snapshot=(JsonObject)(current.Object("previousAdjustment") ?? throw new ServiceException("没有可以撤销的快捷调整。")).DeepClone();snapshot["title"]=current.Str("title");
                TripContractValidator.Validate(snapshot);
                return Results.Json(await trips.SaveAsync(snapshot,input.Int("expectedRevision"),context.RequestAborted));
            }
            JsonObject? original=null;HashSet<int>? adjustedDays=null;
            if(input["adjustedDay"] is not null || input["adjustedDays"] is not null) {
                adjustedDays=[];
                if(input["adjustedDays"] is not null) {
                    if(input["adjustedDays"] is not JsonArray list || list.Count is 0 or >15)throw new ServiceException("调整日期无效。");
                    foreach(var item in list){if(item is not JsonValue v || !v.TryGetValue<int>(out var day) || day<0)throw new ServiceException("调整日期无效。");adjustedDays.Add(day);}
                } else {
                    if(input["adjustedDay"] is not JsonValue d || !d.TryGetValue<int>(out var dayIndex) || dayIndex<0)throw new ServiceException("调整日期无效。");
                    adjustedDays.Add(dayIndex);
                }
                original=await trips.FindAsync(trip.Str("id"),context.RequestAborted) ?? throw new ServiceException("原行程不存在。");
                if(original.Int("revision")!=input.Int("expectedRevision"))throw new RevisionConflictException();
                if(adjustedDays.Any(day=>day>=original.Array("days")!.Count) || trip.Object("schedule") is null)throw new ServiceException("请先完成当天调整试算。");
                // Partial schedule edits retain the accepted profile and untouched days.
                trip["profile"]=original["profile"]!.DeepClone();
                foreach(var selected in adjustedDays){var old=original.Array("days")![selected]!.AsObject();foreach(var key in new[]{"completedStops","skippedStops","planningStartMinute","executionStarted"}){trip.Array("days")![selected]!.AsObject().Remove(key);if(old[key] is not null)trip.Array("days")![selected]![key]=old[key]!.DeepClone();}}
            }
            if(trip.Object("schedule") is { } schedule) {
                if(schedule.Object("settings") is null || schedule.Array("days") is null)throw new ServiceException("時間軸資料不完整，請重新試算後保存。");
                trip=editor.Build(new JsonObject{["trip"]=trip.DeepClone(),["settings"]=schedule["settings"]?.DeepClone(),["days"]=schedule["days"]?.DeepClone(),["version"]=schedule.Int("version"),["rainy"]=schedule.Bool("rainy")});
                if(trip.Object("schedule")!.Array("issues")!.OfType<JsonObject>().Any(x=>x.Bool("blocking") && (adjustedDays is null || adjustedDays.Contains(x.Int("day")) || x.Int("day")==-1)))throw new ServiceException("時間軸仍有營業、預約或必要任務衝突，請先修改草案。");
            }
            if(original is not null && adjustedDays is not null) {
                var result=(JsonObject)original.DeepClone();
                if(trip.Array("walkingRoutes") is { } roads)result["walkingRoutes"]=roads.DeepClone();
                if(trip.Array("transportRoutes") is {} transit)result["transportRoutes"]=transit.DeepClone();
                foreach(var selected in adjustedDays)result.Array("days")![selected]=trip.Array("days")![selected]!.DeepClone();
                if(result.Object("schedule") is null) {
                    result["schedule"]=trip["schedule"]!.DeepClone();
                    var canonical=result.Object("schedule")!;
                    for(var i=0;i<result.Array("days")!.Count;i++)if(!adjustedDays.Contains(i))canonical.Array("days")![i]=PreservedDay(result.Array("days")![i]!.AsObject(),result.Object("profile")!);
                    canonical["issues"]=new JsonArray();
                }
                if(result.Object("schedule") is { } oldSchedule) {
                    foreach(var selected in adjustedDays)oldSchedule.Array("days")![selected]=trip.Object("schedule")!.Array("days")![selected]!.DeepClone();
                    oldSchedule["totalCost"]=result.Array("days")!.OfType<JsonObject>().Sum(d=>d.Number("estimatedCost"));
                    var issues=new JsonArray(oldSchedule.Array("issues")!.OfType<JsonObject>().Where(i=>i.Int("day")!=-1 && !adjustedDays.Contains(i.Int("day"))).Select(i=>i.DeepClone()).ToArray());
                    foreach(var issue in trip.Object("schedule")!.Array("issues")!.OfType<JsonObject>().Where(i=>i.Int("day")==-1 || adjustedDays.Contains(i.Int("day"))))issues.Add(issue.DeepClone());
                    oldSchedule["issues"]=issues;oldSchedule["version"]=trip.Object("schedule")!.Int("version");
                }
                var snapshot=(JsonObject)original.DeepClone();snapshot.Remove("previousAdjustment");result["previousAdjustment"]=snapshot;
                trip=result;
            }
            TripContractValidator.Validate(trip);
            if (input["expectedRevision"] is not null &&
                (input["expectedRevision"] is not JsonValue revision || !revision.TryGetValue<int>(out _)))
                return Results.Json(new { error = "行程版本必须是整数" }, statusCode: 400);
            return Results.Json(await trips.SaveAsync(trip, input.Int("expectedRevision"), context.RequestAborted));
        });
        routes.MapDelete("/trips/{id}", async (string id, TripRepository trips, CancellationToken ct) =>
        {
            await trips.DeleteAsync(id, ct);
            return Results.Json(new { deleted = true });
        });
    }
    private static JsonObject PreservedDay(JsonObject day,JsonObject profile) {
        int Minute(string time){var parts=time.Split(':');return parts.Length==2 && int.TryParse(parts[0],out var h) && int.TryParse(parts[1],out var m) ? h*60+m:profile.Int("startMinute",600);}
        var activities=new JsonArray();var stops=day.Array("stops")!;var legs=day.Array("legs")!;
        for(var i=0;i<stops.Count;i++){
            var stop=stops[i]!.AsObject();activities.Add(new JsonObject{
                ["id"]=stop.Str("id"),["placeId"]=stop.Str("id"),["kind"]="attraction",["name"]=stop.Str("name"),["lat"]=stop.Number("lat"),["lng"]=stop.Number("lng"),
                ["duration"]=stop.Int("visitMinutes"),["cost"]=stop.Number("cost"),["queueMinutes"]=null,["queueSource"]="未知",["arrival"]=stop.Str("arrival"),["departure"]=stop.Str("departure"),["scheduledStart"]=Minute(stop.Str("arrival")),["travelMinutes"]=legs[i]!.AsObject().Int("minutes"),["indoor"]=stop.Bool("indoor"),["hoursVerified"]=stop.Bool("hoursVerified")});
        }
        var transport=legs.OfType<JsonObject>().Sum(l=>l.Int("minutes"));
        var stay=(JsonObject)day["end"]!.DeepClone();stay["cost"]=0;stay["confirmed"]=false;stay["checkIn"]=900;stay["checkOut"]=660;
        return new JsonObject{["date"]=day.Str("date"),["startMinute"]=profile.Int("startMinute",600),["endMinute"]=profile.Int("endMinute",1080),["start"]=day["start"]!.DeepClone(),["end"]=day["end"]!.DeepClone(),["stay"]=stay,["activities"]=activities,["transportMinutes"]=transport,["queueMinutes"]=0,["timeUsed"]=transport,["timeRemaining"]=180-transport,["unknownQueue"]=activities.Count>0,["estimatedSteps"]=day.Int("estimatedSteps"),["estimatedCost"]=day.Number("estimatedCost"),["finishTime"]=day.Str("finishTime")};
    }

}
