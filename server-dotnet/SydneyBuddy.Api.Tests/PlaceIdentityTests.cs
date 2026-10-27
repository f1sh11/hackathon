using System.Text.Json.Nodes;
namespace SydneyBuddy.Api.Tests;
public sealed class PlaceIdentityTests
{
    static JsonObject Point(string id,string name,string source="OpenStreetMap",double lat=-33.8717) => new()
    { ["id"]=id,["name"]=name,["source"]=source,["lat"]=lat,["lng"]=151.2067,["category"]="文化",["poiKind"]="historic_site" };

    [Fact] public void BilingualOsmNodeAndBuildingBecomeOneEntryWithBothSourceIds()
    {
        var a=Point("osm:node:2250356920","Queen Victoria Building");
        var b=Point("osm:way:40717424","维多利亚女王大厦");b["nameEn"]="Queen Victoria Building";
        var p=Assert.Single(UnifiedPoiService.Merge([a,b],-33.8717,151.2067,500,"all","",default).Array("places")!)!.AsObject();
        Assert.Equal(2,p.Array("sourceIds")!.Count);
    }
    [Fact] public void SimilarNamesAndNearbyBranchesAreNotIdentityEvidence()
    {
        Assert.False(PlaceIdentity.Same(Point("a","Sydney Museum"),Point("b","Sydney Museum Cafe")));
        Assert.False(PlaceIdentity.Same(Point("a","Cafe"),Point("b","Cafe",lat:-33.8718)));
        var a=Point("osm:node:1","Cafe");a["address"]="1 George St";
        var b=Point("osm:way:2","Cafe");b["address"]="2 George St";
        Assert.False(PlaceIdentity.Same(a,b));
    }
    [Fact] public void CommonWebsiteOrBuildingCoordinatesDoNotMergeDifferentVenues()
    {
        var a=Point("a","Garden");var b=Point("b","Visitor Centre");
        a["website"]="https://example.com";b["website"]="https://example.com";
        Assert.False(PlaceIdentity.Same(a,b));
    }
    [Fact] public void MissingCoordinatesNeverCountAsNearby()
    {
        var a=Point("a","Museum","Catalog");var b=Point("b","Museum");a.Remove("lat");b.Remove("lat");
        Assert.False(PlaceIdentity.Same(a,b));
    }
    [Fact] public void MergedSourceIdsIdentifyTheSamePlaceInSavedTrips()
    {
        var a=Point("catalog:qvb","维多利亚女王大厦");a["sourceIds"]=new JsonArray("osm:way:40717424");
        Assert.True(PlaceIdentity.Same(a,Point("osm:way:40717424","Queen Victoria Building")));
    }
    [Fact] public void FinalFeasibilityRejectsTranslatedAliasWithAnotherId()
    {
        var trip=FeasibilityAgentTests.Draft();var day=trip.Array("days")![0]!.AsObject();
        var first=day.Array("stops")![0]!.AsObject();first["source"]="Catalog";first["nameEn"]="Sydney Opera House";
        var alias=first.DeepClone().AsObject();alias["id"]="osm:way:42";alias["name"]="雪梨歌劇院";alias["source"]="OpenStreetMap";
        day.Array("stops")!.Add(alias);day.Array("legs")!.Add(day.Array("legs")![0]!.DeepClone());
        Assert.Contains(FeasibilityTools.Constraints(trip,trip.Object("profile")!).Array("issues")!.OfType<JsonObject>(),i=>i.Str("code")=="duplicate_place");
    }
}
