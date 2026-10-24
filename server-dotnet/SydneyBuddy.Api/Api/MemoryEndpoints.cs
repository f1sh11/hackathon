namespace SydneyBuddy.Api;

/// <summary>授权偏好的 REST 入口；preference endpoints delegate cross-store coordination to a service.</summary>
public static class MemoryEndpoints
{
    public static void MapMemoryEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/memories", async (MemoryRepository records, CancellationToken ct) => Results.Json(await records.ListAsync(ct)));
        routes.MapPost("/memories", async (HttpContext context, PreferenceService preferences) =>
            Results.Json(await preferences.SaveAsync(await RequestJson.ReadAsync(context), context.RequestAborted)));
        routes.MapPut("/memories/{id}", async (string id, HttpContext context, PreferenceService preferences) =>
            Results.Json(await preferences.UpdateAsync(id, await RequestJson.ReadAsync(context), context.RequestAborted)));
        routes.MapDelete("/memories/{id}", async (string id, PreferenceService preferences, CancellationToken ct) =>
        {
            await preferences.DeleteAsync(id, ct);
            return Results.Json(new { deleted = true });
        });
    }
}
