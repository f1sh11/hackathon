using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>Headless World Model API: simulation writes append-only state; clients query projections and record choices.</summary>
public static class WorldEndpoints
{
    public static void MapWorldEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/trips/{tripId}/world", async (string tripId, WorldRepository worlds, CancellationToken ct) =>
            Results.Json(await worlds.GetLatestForTripAsync(tripId, ct)));
        routes.MapGet("/trips/{tripId}/planning-state",async(string tripId,WorldRepository worlds,CancellationToken ct)=>
            Results.Json(await worlds.GetLatestForTripAsync(tripId,ct,"itinerary_planning")));
        routes.MapPost("/trips/{tripId}/simulation-runs", async (string tripId, HttpContext context, TravelWorldModelService simulation) =>
        {
            var input = await RequestJson.ReadAsync(context);
            var requestId = input.Str("requestId");
            if (requestId.Length == 0) throw new ServiceException("请提供模拟请求编号，以便安全重试。");
            return Results.Json(await simulation.SimulateAsync(tripId, requestId, context.RequestAborted));
        });
        routes.MapGet("/simulation-runs/{runId}", async (string runId, WorldRepository worlds, CancellationToken ct) =>
            Results.Json(await worlds.GetRunAsync(runId, ct)));
        routes.MapPost("/simulation-runs/{runId}/decisions", async (string runId, HttpContext context, TravelWorldModelService simulation) =>
        {
            var input = await RequestJson.ReadAsync(context);
            return Results.Json(await simulation.DecideAsync(runId, input.Str("suggestionId"), input.Str("decision"), context.RequestAborted));
        });
    }
}
