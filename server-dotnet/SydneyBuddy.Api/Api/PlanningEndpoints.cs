using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>规划与天气 HTTP 边界；planning/weather REST contracts delegate domain work to services.</summary>
public static class PlanningEndpoints
{
    public static void MapPlanningEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/health", (SecretStore secrets, PaidServicePolicy paidServices, NavigationService navigation, JourneyService journeys) =>
        {
            var config = secrets.Read();
            return Results.Json(new
            {
                navigationConfigured = navigation.Configured, transitConfigured = journeys.TransitConfigured, status = "ok", database = "postgresql", paidServicesAllowed = paidServices.Allowed,
                nvidiaConfigured = config["NVIDIA_API_KEY"].Length > 0,
                nswTransitConfigured = config["TFNSW_API_KEY"].Length > 0,
                googleConfigured = config["GOOGLE_SERVER_KEY"].Length > 0,
                iosMapConfigured = config["GOOGLE_IOS_KEY"].Length > 0,
                model = config["NVIDIA_MODEL"].Length > 0 ? config["NVIDIA_MODEL"] : SecretStore.DefaultModel
            });
        });
        routes.MapPost("/navigation/route", async (HttpContext context, NavigationService navigation) =>
            Results.Json(await navigation.RouteAsync(await RequestJson.ReadAsync(context), context.RequestAborted)));
        routes.MapPost("/journey/plan", async (HttpContext context, JourneyService journeys) =>
            Results.Json(await journeys.PlanAsync(await RequestJson.ReadAsync(context), context.RequestAborted)));
        routes.MapPost("/poi/search",async (HttpContext context,PoiService pois)=>Results.Json(await pois.SearchAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapPost("/poi/media",async (HttpContext context,PoiMediaService media)=>Results.Json(await media.LoadAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapGet("/data/map-layers",(TravelDataContext travel,ProjectPaths paths)=>Results.Json(new JsonObject{["dataSource"]=travel.Mode,["layers"]=travel.Warehouse?travel.Snapshot!.Maps.DeepClone():JsonNode.Parse(File.ReadAllText(Path.Combine(paths.RepoRoot,"shared","travel-map-layers.json")))}));
        routes.MapPost("/data/insights",async (HttpContext context,TravelInsightService insights)=>{context.Response.Headers.CacheControl="no-store";return Results.Json(await insights.GetAsync(await RequestJson.ReadAsync(context),context.RequestAborted));});
        routes.MapGet("/data/status",(TravelDataContext travel,PlaceCatalog catalog)=>Results.Json(new {dataSource=travel.Mode,status="ok",catalogCount=catalog.Get().Count,readAt=travel.Snapshot?.ReadAt,poiCount=travel.Snapshot?.Poi.Array("places")?.Count,hiddenGemCount=travel.Snapshot?.Gems?.Count,osmCount=travel.Snapshot?.Osm?.Count}));
        routes.MapPost("/schedule/editor", async (HttpContext context, TransportScheduleService editor) => Results.Json(await editor.BuildAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapGet("/catalog", (Planner planner) => Results.Json(planner.Catalog));
        routes.MapPost("/plan/remaining",async (HttpContext context,TripRepository trips,RemainingJourneyService remaining)=>{var input=await RequestJson.ReadAsync(context);var original=await trips.FindAsync(input.Str("tripId"),context.RequestAborted)??throw new ServiceException("行程不存在。");if(original.Int("revision")!=input.Int("expectedRevision"))throw new RevisionConflictException();return Results.Json(await remaining.BuildAsync(original,input,context.RequestAborted));});
        routes.MapPost("/plan/prepare",async (HttpContext context,Planner planner)=>Results.Json(await planner.PrepareWalkingAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapPost("/plan", async (HttpContext context, Planner planner, PaidServicePolicy paidServices) =>
        {
            var input = await RequestJson.ReadAsync(context);
            if (input.ContainsKey("mode") &&
                (input["mode"] is not JsonValue value || !value.TryGetValue<string>(out _)))
                throw new ServiceException("模式不合法");
            var mode = input.Str("mode", "demo");
            if (mode is not ("demo" or "assisted" or "live")) throw new ServiceException("模式不合法");
            if (mode != "demo") paidServices.RequireAllowed();
            input.Remove("mode");
            return Results.Json(await planner.PlanAsync(input, mode, context.RequestAborted));
        });
        routes.MapPost("/weather", async (HttpContext context, Planner planner, PaidServicePolicy paidServices) =>
        {
            context.Response.Headers.CacheControl="no-store";
            if(!context.Request.Headers.ContainsKey(TravelDataContext.Header))paidServices.RequireAllowed();
            return Results.Json(await planner.WeatherAsync(await RequestJson.ReadAsync(context), context.RequestAborted));
        });
    }
}
