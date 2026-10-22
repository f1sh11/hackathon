using Npgsql;
using Roam.Api.Infrastructure.Persistence;

namespace Roam.Api.Features.System;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", async (TripStore db, CancellationToken ct) =>
        {
            try
            {
                return await db.IsHealthyAsync(ct)
                    ? Results.Ok(new { status = "healthy", database = "PostgreSQL", backend = ".NET 10", demoData = true })
                    : Results.StatusCode(503);
            }
            catch (NpgsqlException)
            {
                return Results.Json(new { status = "unhealthy", database = "PostgreSQL" }, statusCode: 503);
            }
        });

        app.MapGet("/api/bootstrap", async (TripStore db, CancellationToken ct) => Results.Ok(new
        {
            profile = await db.GetProfileAsync(ct),
            trips = await db.ListTripsAsync(ct),
            demoData = true,
            dataNotice = "示例地点、费用、营业时间、交通与 HP 均用于产品测试，未连接外部旅行 API。"
        }));

        return app;
    }
}
