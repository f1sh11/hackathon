using Microsoft.EntityFrameworkCore;

namespace SydneyBuddy.Api;

/// <summary>集中声明依赖与生命周期；declares lifetimes without mixing startup with business logic.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddBuddyServices(this IServiceCollection services)
    {
        services.AddSingleton(provider => new ProjectPaths(provider.GetRequiredService<IConfiguration>()["SYDNEYBUDDY_ROOT"] ?? ProjectPaths.DiscoverRoot()));
        services.AddSingleton<SecretStore>();
        services.AddSingleton<PaidServicePolicy>();
        services.AddHttpClient<NavigationService>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<JourneyService>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<GoogleJourneyProvider>(client => client.Timeout = TimeSpan.FromSeconds(25))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<NswJourneyProvider>(client => client.Timeout = TimeSpan.FromSeconds(25))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddTransient<IJourneyProvider>(p => p.GetRequiredService<GoogleJourneyProvider>());
        services.AddTransient<IJourneyProvider>(p => p.GetRequiredService<NswJourneyProvider>());
        // DbContext 以请求为单位，不能作为单例共享。 / DbContext is scoped, never shared across requests.
        services.AddDbContext<BuddyDbContext>((provider, options) =>
        {
            var connection = provider.GetRequiredService<IConfiguration>().GetConnectionString("SydneyBuddy")
                ?? "Host=127.0.0.1;Port=55432;Database=sydneybuddy;Username=sydneybuddy;Password=sydneybuddy_dev;Maximum Pool Size=20";
            options.UseNpgsql(connection);
        });
        services.AddScoped<TripRepository>();
        services.AddScoped<MemoryRepository>();
        services.AddHttpClient<IExternalJsonClient, ExternalJsonClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddScoped<PreferenceService>();
        services.AddSingleton<PetSessionStore>();
        services.AddHttpClient<IPetModel, PetModel>(client => client.Timeout = Timeout.InfiniteTimeSpan).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<WeatherService>();
        services.AddMemoryCache();
        services.AddSingleton<CatalogCache>();
        services.AddSingleton<PlaceCatalog>();
        services.AddScoped<PlaceService>();
        services.AddScoped<RecommendationService>();
        services.AddScoped<RouteService>();
        services.AddScoped<RouteScheduler>();
        services.AddScoped<Planner>();
        services.AddScoped<ScheduleEditorService>();
        services.AddHttpClient<OsmPoiProvider>(client=>client.Timeout=TimeSpan.FromSeconds(25)).ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false});
        services.AddSingleton<OverturePoiProvider>();
        services.AddScoped<IPoiProvider>(p=>p.GetRequiredService<IConfiguration>()["Poi:OpenProvider"]=="overpass" ? p.GetRequiredService<OsmPoiProvider>() : p.GetRequiredService<OverturePoiProvider>());
        services.AddScoped<GooglePoiProvider>();
        services.AddScoped<PoiService>();
        services.AddHttpClient<PoiMediaService>(client=>client.Timeout=TimeSpan.FromSeconds(18)).ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false});
        return services;
    }
}
