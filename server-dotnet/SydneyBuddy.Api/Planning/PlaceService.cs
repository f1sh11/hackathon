using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 从只读目录取得候选地点，live 模式再逐个补齐 Google 坐标/营业时段；不缓存外部响应。
/// Loads catalog candidates and enriches live evidence from Google; provider responses are never cached here.
/// </summary>
public sealed class PlaceService(IExternalJsonClient external, PlaceCatalog catalog,TravelDataContext? travel=null)
{
    public IReadOnlyList<JsonObject> Catalog => catalog.Get();

    public async Task<JsonObject[]> GetCandidatesAsync(PlanningContext context, CancellationToken ct = default)
    {
        var places = catalog.Get().ToArray();
        if (context.Mode != "live" || travel?.Warehouse==true)
        {
            context.Record("Load candidate places", $"{places.Length} Sydney candidate places, including specific restaurants. See catalog sources. Prices, opening windows and visit durations are planning estimates.", travel?.Warehouse==true ? "Zetaris / travel_catalog":"Local sample data");
            return places;
        }
        var key = context.Config.GetValueOrDefault("GOOGLE_SERVER_KEY");
        if (string.IsNullOrEmpty(key)) throw new ServiceException("The Google server key is not configured. Set it on the computer's local setup page.");
        foreach (var place in places)
        {
            var result = await external.FetchAsync("https://places.googleapis.com/v1/places:searchText", new JsonObject
            {
                ["textQuery"] = place.Str("query"), ["maxResultCount"] = 1, ["languageCode"] = "en",
                ["locationBias"] = new JsonObject
                {
                    ["circle"] = new JsonObject
                    {
                        ["center"] = new JsonObject { ["latitude"] = -33.8688, ["longitude"] = 151.2093 },
                        ["radius"] = 20000
                    }
                }
            }, new Dictionary<string, string>
            {
                ["X-Goog-Api-Key"] = key,
                ["X-Goog-FieldMask"] = "places.id,places.displayName,places.location,places.regularOpeningHours,places.googleMapsUri"
            }, cancellationToken: ct);
            var item = result.Array("places")?.FirstOrDefault() as JsonObject;
            if (item is null) throw new ServiceException("Google returned incomplete candidate details. A plan labelled live cannot be generated.");
            var location = item.Object("location");
            if (location?["latitude"] is null || location["longitude"] is null || string.IsNullOrEmpty(item.Str("id")))
                throw new ServiceException("Google returned incomplete candidate details. A plan labelled live cannot be generated.");
            var latitude = location.Number("latitude", double.NaN);
            var longitude = location.Number("longitude", double.NaN);
            if (!double.IsFinite(latitude) || !double.IsFinite(longitude))
                throw new ServiceException("Google returned incomplete candidate details. A plan labelled live cannot be generated.");
            var coordinates = new JsonObject { ["lat"] = latitude, ["lng"] = longitude };
            if (RouteService.Distance(place, coordinates) > 1500)
                throw new ServiceException("The place search result does not match the expected landmark. Planning stopped.");
            place["lat"] = coordinates.Number("lat");
            place["lng"] = coordinates.Number("lng");
            place["googlePlaceId"] = item.Str("id");
            place["mapsURL"] = item.Str("googleMapsUri");
            var hours = item.Object("regularOpeningHours");
            place["periods"] = hours is null ? null : (hours.Array("periods")?.DeepClone() ?? new JsonArray());
            place["hoursSource"] = hours is null ? "Opening hours unavailable; using demo assumptions" : "Google regular opening hours; holidays may differ";
        }
        context.Record("Look up places and opening hours", "Google Places returned coordinates and available regular opening hours. Costs remain estimates.", "Google Places API");
        return places;
    }

}
