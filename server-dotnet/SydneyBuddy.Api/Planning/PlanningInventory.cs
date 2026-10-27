using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// Request-scoped, bounded lists from the selected local/Zetaris source, not model-generated places.
public sealed class PlanningInventory(UnifiedPoiService pois, TravelDataContext source)
{
    public JsonObject Read(JsonObject profile, CancellationToken ct)
    {
        var start = profile.Object("hotel") ?? profile.Object("start") ?? new JsonObject { ["lat"] = -33.8611, ["lng"] = 151.2126 };
        var lat = start.Number("lat", double.NaN); var lng = start.Number("lng", double.NaN);
        if (!double.IsFinite(lat) || !double.IsFinite(lng) || lat is < -37.5 or > -28 || lng is < 141 or > 154)
            throw new ServiceException("請提供 NSW 範圍內的住宿或出發地點。");
        JsonArray Search(string category, int count) => new(pois.Search(lat, lng, 15000, category, "", ct)
            .Array("places")!.OfType<JsonObject>().Take(count).Select(p => p.DeepClone()).ToArray());
        var activities = Search("sights", 30);
        foreach (var p in Search("food", 15)) activities.Add(p!.DeepClone());
        return new() { ["dataSource"] = source.Mode, ["radiusMeters"] = 15000,
            ["activities"] = activities, ["accommodations"] = Search("lodging", 15),
            ["availabilityVerified"] = false, ["pricesVerified"] = false };
    }

    public static bool IsAccommodation(JsonObject place) => place.Str("category") is "住宿" or "lodging" ||
        place.Str("searchCategory") == "lodging" || place.Str("poiKind") is "hotel" or "hostel" or "motel" or "guest_house" or "resort_hotel" or "camp_site" or "caravan_site";
}
