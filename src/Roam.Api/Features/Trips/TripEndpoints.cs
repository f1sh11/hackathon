using Roam.Api.Contracts;
using Roam.Api.Infrastructure.Errors;
using Roam.Api.Infrastructure.Persistence;
using Roam.Core;

namespace Roam.Api.Features.Trips;

public static class TripEndpoints
{
    public static IEndpointRouteBuilder MapTripEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/trips", async (CreateTripRequest input, TripStore db, Planner planner, CancellationToken ct) =>
        {
            var snapshot = TripRequestValidation.Validate(input);
            var profile = await db.GetProfileAsync(ct) ?? throw new ConflictException("请先创建旅行角色。");
            if (profile.Id != snapshot.ProfileId) throw new ConflictException("画像编号不匹配，请刷新后重试。");
            var result = planner.Generate(profile, TripRequestValidation.ToPlanning(snapshot, false));
            return Results.Ok(await db.CreateTripAsync(snapshot, input.IdempotencyKey, result, ct));
        });

        app.MapGet("/api/trips/{id:guid}", async (Guid id, TripStore db, CancellationToken ct) =>
            Results.Ok(await db.GetTripAsync(id, ct)));

        app.MapPost("/api/trips/{id:guid}/replan", async (Guid id, ReplanRequest input, TripStore db, Planner planner, CancellationToken ct) =>
        {
            if (input.IdempotencyKey == Guid.Empty) throw new ArgumentException("重新规划需要有效的请求编号。");
            var trip = await db.GetTripAsync(id, ct);
            var profile = await db.GetProfileAsync(ct) ?? throw new MissingException("没有找到旅行画像。");
            var result = planner.Generate(profile, TripRequestValidation.ToPlanning(trip.Request, input.Easier));
            return Results.Ok(await db.ReplanAsync(id, input.IdempotencyKey, input.Easier, result, ct));
        });

        return app;
    }
}
