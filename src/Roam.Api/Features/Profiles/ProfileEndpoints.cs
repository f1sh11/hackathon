using Roam.Api.Contracts;
using Roam.Api.Infrastructure.Persistence;

namespace Roam.Api.Features.Profiles;

public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/profiles", async (CreateProfileRequest request, TripStore db, CancellationToken ct) =>
        {
            if (request.Answers is null) throw new ArgumentException("请回答全部六个问题。");
            return Results.Ok(await db.CreateProfileAsync(request.Answers, ct));
        });

        return app;
    }
}
