using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>规划与天气 HTTP 边界；planning/weather REST contracts delegate domain work to services.</summary>
public static class PlanningEndpoints
{
    public static void MapPlanningEndpoints(this RouteGroupBuilder routes)
    {
        routes.MapGet("/planning/runs/{id}",(string id,PlanningRunJournal journal)=>Results.Json(journal.Read(id)));
        routes.MapGet("/health", (SecretStore secrets, PaidServicePolicy paidServices, NavigationService navigation, JourneyService journeys, TravelDataContext travel,PlanningAgent agent) =>
        {
            var config = secrets.Read();
            return Results.Json(new
            {
                managedTravelDataSource = travel.ManagedMode,
                navigationConfigured = navigation.Configured, transitConfigured = journeys.TransitConfigured || config["TFNSW_API_KEY"].Length > 0, status = "ok", database = "postgresql", paidServicesAllowed = paidServices.Allowed,
                nvidiaConfigured = ModelProviderSettings.Configured(config),
                aiProvider = string.IsNullOrWhiteSpace(config["AI_PROVIDER"])?"nvidia":config["AI_PROVIDER"],
                nightshiftConfigured = config["AI_PROVIDER"]=="nightshift" && ModelProviderSettings.Configured(config),
                planningAgentProvider = agent.Provider, planningAgentConfigured = agent.Configured,
                nswTransitConfigured = config["TFNSW_API_KEY"].Length > 0,
                googleConfigured = config["GOOGLE_SERVER_KEY"].Length > 0,
                iosMapConfigured = config["GOOGLE_IOS_KEY"].Length > 0,
                model = ModelProviderSettings.Configured(config) ? ModelProviderSettings.Resolve(config).Model : "unconfigured"
            });
        });
        routes.MapGet("/planning/agent/status",async (PlanningAgentSettings settings,MeterlessRuntime runtime,PlanningAgent agent,HttpContext context)=>{
            context.Response.Headers.CacheControl="no-store";
            return Results.Json(new JsonObject{["provider"]=settings.Provider,["skillVersion"]=ItinerarySkill.Version,["skillSource"]=ItinerarySkill.Source,["baseSkillVersion"]=ItinerarySkill.BaseVersion,["configured"]=agent.Configured,
                ["cloudModel"]=settings.Provider=="meterless-cloud"?new JsonObject{["provider"]="nvidia",["status"]=agent.Configured?"configured_not_probed":"not_configured",["memoryEngine"]="Meterless H-MEM",["stateEngine"]="Meterless World Model / application implementation"}:null,
                ["runtime"]=settings.Provider=="meterless"?await runtime.StatusAsync(context.RequestAborted):new JsonObject{["status"]="not_selected"}});
        });
        routes.MapPost("/planning/inventory",async(HttpContext context,PlanningInventory inventory)=>Results.Json(inventory.Read(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapPost("/navigation/route", async (HttpContext context, NavigationService navigation) =>
            Results.Json(await navigation.RouteAsync(await RequestJson.ReadAsync(context), context.RequestAborted)));
        routes.MapPost("/journey/plan", async (HttpContext context, JourneyService journeys) =>
            Results.Json(await journeys.PlanAsync(await RequestJson.ReadAsync(context), context.RequestAborted)));
        routes.MapPost("/restaurants/parse-menu",async(HttpContext context)=>Results.Json(RestaurantEvidence.ParseMenu(await RequestJson.ReadAsync(context))));
        routes.MapPost("/restaurants/import",async(HttpContext context,RestaurantService service)=>Results.Json(await service.ImportAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapPost("/restaurants/estimate",async(HttpContext context,RestaurantService service)=>Results.Json(await service.EstimateAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapPost("/activities/summaries",async(HttpContext context,ActivityOfferService service)=>Results.Json(await service.SummariesAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapPost("/activities/search",async(HttpContext context,ActivityOfferService service)=>Results.Json(await service.SearchAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapGet("/restaurants/published",(RestaurantStore store)=>Results.Json(new{places=store.Published()}));
        routes.MapPost("/social/extract",async(HttpContext context,SocialImportService service)=>Results.Json(await service.ExtractAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapPost("/social/import",async(HttpContext context,SocialImportService service)=>Results.Json(await service.ImportAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapGet("/social/published",(SocialImportStore store)=>Results.Json(new{places=store.Published()}));
        routes.MapGet("/social/imports",(SocialImportStore store)=>Results.Json(new{imports=store.List()}));
        routes.MapPost("/social/{id}/publish",async(string id,HttpContext context,SocialImportService service)=>Results.Json(await service.PublishAsync(id,await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapPost("/poi/search",async (HttpContext context,PoiService pois)=>Results.Json(await pois.SearchAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapPost("/poi/media",async (HttpContext context,PoiMediaService media)=>Results.Json(await media.LoadAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapGet("/data/map-layers",(TravelDataContext travel,ProjectPaths paths)=>Results.Json(new JsonObject{["dataSource"]=travel.Mode,["layers"]=travel.Warehouse?travel.Snapshot!.Maps.DeepClone():JsonNode.Parse(File.ReadAllText(Path.Combine(paths.RepoRoot,"shared","travel-map-layers.json")))}));
        routes.MapPost("/data/insights",async (HttpContext context,TravelInsightService insights)=>{context.Response.Headers.CacheControl="no-store";return Results.Json(await insights.GetAsync(await RequestJson.ReadAsync(context),context.RequestAborted));});
        routes.MapGet("/data/status",(TravelDataContext travel,PlaceCatalog catalog)=>Results.Json(new {dataSource=travel.Mode,status="ok",catalogCount=catalog.Get().Count,readAt=travel.Snapshot?.ReadAt,poiCount=travel.Snapshot?.Poi.Array("places")?.Count,hiddenGemCount=travel.Snapshot?.Gems?.Count,osmCount=travel.Snapshot?.Osm?.Count}));
        routes.MapPost("/schedule/editor", async (HttpContext context, TransportScheduleService editor) => Results.Json(await editor.BuildAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapGet("/catalog", (Planner planner) => Results.Json(planner.Catalog));
        routes.MapPost("/plan/remaining",async (HttpContext context,TripRepository trips,RemainingJourneyService remaining)=>{var input=await RequestJson.ReadAsync(context);var original=await trips.FindAsync(input.Str("tripId"),context.RequestAborted)??throw new ServiceException("行程不存在。");if(original.Int("revision")!=input.Int("expectedRevision"))throw new RevisionConflictException();return Results.Json(await remaining.BuildAsync(original,input,context.RequestAborted));});
        routes.MapPost("/plan/prepare",async (HttpContext context,Planner planner)=>Results.Json(await planner.PrepareWalkingAsync(await RequestJson.ReadAsync(context),context.RequestAborted)));
        routes.MapPost("/plan", async (HttpContext context, Planner planner, PaidServicePolicy paidServices,PlanningAgent agent) =>
        {
            return Results.Json(await CreateDraftAsync(await RequestJson.ReadAsync(context),planner,paidServices,agent,context.RequestAborted));
        });
        routes.MapPost("/weather", async (HttpContext context, Planner planner, PaidServicePolicy paidServices,TravelDataContext travel) =>
        {
            context.Response.Headers.CacheControl="no-store";
            if(!travel.Explicit)paidServices.RequireAllowed();
            return Results.Json(await planner.WeatherAsync(await RequestJson.ReadAsync(context), context.RequestAborted));
        });
    }
    // Both App and file tests use the same mode validation and paid-service policy.
    public static Task<JsonObject> CreateDraftAsync(JsonObject input,Planner planner,PaidServicePolicy paidServices,PlanningAgent agent,CancellationToken ct)
    {
        input=input.DeepClone().AsObject();
        if(input.ContainsKey("mode") && (input["mode"] is not JsonValue value || !value.TryGetValue<string>(out _)))
            throw new ServiceException("模式不合法");
        var mode=input.Str("mode","demo");
        if(mode is not ("demo" or "assisted" or "live"))throw new ServiceException("模式不合法");
        if(mode!="demo" && !(mode=="assisted"&&agent.CloudCallsAllowed))paidServices.RequireAllowed();
        input.Remove("mode");
        return planner.PlanAsync(input,mode,ct);
    }

}
