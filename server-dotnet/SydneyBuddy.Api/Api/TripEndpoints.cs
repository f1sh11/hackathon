using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>行程集合的 REST 入口；trip endpoints validate save contracts before repository calls.</summary>
public static class TripEndpoints
{
    public static void MapTripEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/trips", async (TripRepository trips, CancellationToken ct) => Results.Json(await trips.ListAsync(ct)));
        routes.MapPost("/trips", async (HttpContext context, TripRepository trips, ScheduleEditorService editor) =>
        {
            var input = await RequestJson.ReadAsync(context);
            if (input["trip"] is not JsonObject trip) return Results.Json(new { error = "请求格式不合法" }, statusCode: 400);
            if (trip["id"] is not null && (trip["id"] is not JsonValue id || !id.TryGetValue<string>(out _)))
                return Results.Json(new { error = "行程标识必须是字符串" }, statusCode: 400);
            if(trip.Object("schedule") is { } schedule) {
                if(schedule.Object("settings") is null || schedule.Array("days") is null)throw new ServiceException("時間軸資料不完整，請重新試算後保存。");
                trip=editor.Build(new JsonObject{["trip"]=trip.DeepClone(),["settings"]=schedule["settings"]?.DeepClone(),["days"]=schedule["days"]?.DeepClone(),["version"]=schedule.Int("version"),["rainy"]=schedule.Bool("rainy")});
                if(trip.Object("schedule")!.Array("issues")!.OfType<JsonObject>().Any(x=>x.Bool("blocking")))throw new ServiceException("時間軸仍有營業、預約或必要任務衝突，請先修改草案。");
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
}
