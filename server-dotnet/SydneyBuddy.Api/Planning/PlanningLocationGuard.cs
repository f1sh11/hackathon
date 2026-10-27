using System.Text.Json.Nodes;
namespace SydneyBuddy.Api;

/// Quarantines confirmed source identity/coordinate conflicts from automatic planning only.
/// Evidence and removal criteria: docs/data-quality/KNOWN-LOCATION-CONFLICTS.md.
public static class PlanningLocationGuard
{
    public static bool HasKnownConflict(JsonObject place)
    {
        if(place.Str("id") is not ("overture:6807df81-5509-4b67-8318-8c619ef155eb" or
            "overture:ebf9a059-1f0f-420d-9a95-dea2a5da6424"))return false;
        // Apply only while these exact source identities still claim to be in Sydney.
        return place.Number("lat") is > -34.5 and < -33 && place.Number("lng") is > 150 and < 152.5;
    }
}
