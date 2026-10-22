using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>规划与天气 HTTP 边界；planning/weather REST contracts delegate domain work to services.</summary>
public static class PlanningEndpoints
{
    public static void MapPlanningEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/health", (SecretStore secrets) =>
        {
            var config = secrets.Read();
            return Results.Json(new
            {
                status = "ok", database = "postgresql",
                nvidiaConfigured = config["NVIDIA_API_KEY"].Length > 0,
                googleConfigured = config["GOOGLE_SERVER_KEY"].Length > 0,
                iosMapConfigured = config["GOOGLE_IOS_KEY"].Length > 0,
                model = config["NVIDIA_MODEL"].Length > 0 ? config["NVIDIA_MODEL"] : SecretStore.DefaultModel
            });
        });
        routes.MapGet("/catalog", (Planner planner) => Results.Json(planner.Catalog));
        routes.MapPost("/plan", async (HttpContext context, Planner planner) =>
        {
            var input = await RequestJson.ReadAsync(context);
            if (input.ContainsKey("mode") &&
                (input["mode"] is not JsonValue value || !value.TryGetValue<string>(out _)))
                throw new ServiceException("模式不合法");
            var mode = input.Str("mode", "demo");
            if (mode is not ("demo" or "assisted" or "live")) throw new ServiceException("模式不合法");
            input.Remove("mode");
            return Results.Json(await planner.PlanAsync(input, mode, context.RequestAborted));
        });
        routes.MapPost("/weather", async (HttpContext context, Planner planner) =>
            Results.Json(await planner.WeatherAsync(await RequestJson.ReadAsync(context), context.RequestAborted)));
    }
}
