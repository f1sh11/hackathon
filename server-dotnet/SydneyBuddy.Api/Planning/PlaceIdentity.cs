using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// Conservative physical-place matching. Similar spelling alone never establishes identity.
public static class PlaceIdentity
{
    static readonly string[] NameFields=["name","nameEn","query"];
    static readonly string[] IdFields=["id","placeId","matchedPlaceId"];
    public static string Normalize(string value) {
        if(value.Length==0)return "";
        var result=new StringBuilder(value.Length);
        foreach(var c in value.Normalize(NormalizationForm.FormD))
            if(char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)result.Append(char.ToLowerInvariant(c));
        return result.ToString();
    }
    public static IEnumerable<string> Names(JsonObject p) {
        foreach(var field in NameFields){var raw=p.Str(field);if(raw.Length>0){var name=Normalize(raw);if(name.Length>0)yield return name;}}
        if(p.Array("aliases") is {} aliases)foreach(var alias in aliases){var name=Normalize(alias?.ToString()??"");if(name.Length>0)yield return name;}
    }
    static IEnumerable<string> Ids(JsonObject p) {
        foreach(var field in IdFields){var id=p.Str(field);if(id.Length>0)yield return id;}
        if(p.Array("sourceIds") is {} ids)foreach(var node in ids){var id=node?.ToString()??"";if(id.Length>0)yield return id;}
    }
    public static string[] MatchKeys(JsonObject p) {
        // Most map rows have one name and one ID. Avoid iterator/index allocations per row.
        if(p.Str("nameEn").Length==0 && p.Str("query").Length==0 && p.Array("aliases") is null &&
           p.Str("placeId").Length==0 && p.Str("matchedPlaceId").Length==0 && p.Array("sourceIds") is null && p.Str("wikidata").Length==0)
            return ["name:"+Normalize(p.Str("name")),"id:"+p.Str("id")];
        var keys=new List<string>();
        foreach(var name in Names(p))keys.Add("name:"+name);
        foreach(var id in Ids(p))keys.Add("id:"+id);
        if(p.Str("wikidata").Length>0)keys.Add("wikidata:"+p.Str("wikidata"));
        return keys.ToArray();
    }

    static bool Coordinates(JsonObject p) => double.IsFinite(p.Number("lat", double.NaN)) &&
        double.IsFinite(p.Number("lng", double.NaN)) && Math.Abs(p.Number("lat")) <= 90 && Math.Abs(p.Number("lng")) <= 180;

    public static bool Same(JsonObject a, JsonObject b)
    {
        if (Ids(a).Intersect(Ids(b), StringComparer.Ordinal).Any()) return true;
        if (!Coordinates(a) || !Coordinates(b) || RouteService.Distance(a,b) > 25) return false;
        // A shared building, chain website or phone does not identify the same business.
        var aa=Normalize(a.Str("address")); var ba=Normalize(b.Str("address"));
        if (aa.Length > 0 && ba.Length > 0 && aa != ba) return false;
        if (PlanningInventory.IsAccommodation(a) != PlanningInventory.IsAccommodation(b) ||
            PoiService.ActivityKind(a) != PoiService.ActivityKind(b)) return false;
        if (a.Str("wikidata").Length > 0 && a.Str("wikidata") == b.Str("wikidata")) return true;
        if (!Names(a).Intersect(Names(b), StringComparer.Ordinal).Any()) return false;
        var aid=a.Str("id"); var bid=b.Str("id");
        var osmShapePair=(aid.StartsWith("osm:node:") && bid.StartsWith("osm:way:")) ||
            (bid.StartsWith("osm:node:") && aid.StartsWith("osm:way:"));
        var aSource=a.Str("source",a.Str("query").Length>0?"Catalog":"");
        var bSource=b.Str("source",b.Str("query").Length>0?"Catalog":"");
        return osmShapePair || (aSource.Length > 0 && bSource.Length > 0 && aSource != bSource) ||
            (aa.Length > 0 && aa == ba) || RouteService.Distance(a,b) < 1;
    }
}
