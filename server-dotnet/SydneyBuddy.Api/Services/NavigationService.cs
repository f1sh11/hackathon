using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>Provider boundary for OSM walking navigation. No Google content or credentials cross this boundary.</summary>
public sealed class NavigationService(HttpClient client, IConfiguration configuration)
{
    public bool Configured => Endpoint is not null;
    private Uri? Endpoint => Uri.TryCreate(configuration["Navigation:ValhallaUrl"], UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) ? uri : null;

    public async Task<JsonObject> RouteAsync(JsonObject input, CancellationToken ct)
        => await RouteInternalAsync(input, "pedestrian", ct);

    public Task<JsonObject> PreviewAsync(JsonObject input, string mode, CancellationToken ct) => mode switch
    {
        "walk" => RouteInternalAsync(input, "pedestrian", ct),
        "drive" => RouteInternalAsync(input, "auto", ct),
        _ => throw new ServiceException("The road preview mode is invalid.")
    };

    private async Task<JsonObject> RouteInternalAsync(JsonObject input, string profile, CancellationToken ct)
    {
        var endpoint = Endpoint ?? throw new ServiceException("Walking navigation is not configured. Set Navigation__ValhallaUrl and restart the backend.");
        if (input["locations"] is not JsonArray locations || locations.Count is < 2 or > 30)
            throw new ServiceException("Navigation requires 2–30 coordinate points.");
        var points = new JsonArray();
        foreach (var item in locations)
        {
            if (item is not JsonObject point || !TryNumber(point, "lat", out var lat) || !TryNumber(point, "lon", out var lon)
                || lat is < -90 or > 90 || lon is < -180 or > 180)
                throw new ServiceException("Navigation coordinates are invalid.");
            points.Add(new JsonObject { ["lat"] = lat, ["lon"] = lon, ["type"] = "break" });
        }
        // Only coordinates are accepted from clients. Profiles, upstream URLs and API keys are server-owned.
        var request = new JsonObject
        {
            ["locations"] = points, ["costing"] = profile, ["format"] = "osrm",
            ["shape_format"] = "polyline6", ["banner_instructions"] = true, ["voice_instructions"] = true, ["units"] = "kilometers",
            ["directions_options"] = new JsonObject { ["language"] = "en-US" }
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await client.PostAsJsonAsync(endpoint, request, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new ServiceException("The walking route service is unavailable or the destination cannot be reached. Please try again.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var data = await JsonNode.ParseAsync(stream, cancellationToken: timeout.Token) as JsonObject;
            if (data?.Str("code") != "Ok" || data.Array("routes")?.FirstOrDefault() is not JsonObject route
                || string.IsNullOrWhiteSpace(route.Str("geometry")) || route.Array("legs")?.Count != points.Count - 1)
                throw new ServiceException("Walking route data is incomplete, so navigation cannot start.");
            return data;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ServiceException("The walking route request timed out. Please try again. Your navigation progress is unchanged."); }
        catch (HttpRequestException) { throw new ServiceException("Could not connect to the walking route service. Check the backend configuration."); }
        catch (System.Text.Json.JsonException) { throw new ServiceException("The walking route service returned invalid data."); }
    }

    private static bool TryNumber(JsonObject point, string key, out double number)
    {
        number = 0;
        return point[key] is JsonValue value && value.TryGetValue(out number) && double.IsFinite(number);
    }
}
