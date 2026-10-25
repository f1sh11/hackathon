namespace SydneyBuddy.Api;

/// <summary>组合各功能的 REST 路由；composes focused endpoint modules for Android and iOS clients.</summary>
public static class ApiEndpoints
{
    public static void MapBuddyApi(this WebApplication app, string prefix)
    {
        var routes = app.MapGroup(prefix);
        routes.MapPlanningEndpoints();
        routes.MapTripEndpoints();
        routes.MapWorldEndpoints();
        routes.MapMemoryEndpoints();
        routes.MapPetEndpoints();
        routes.MapSettingsEndpoints();
    }
}
