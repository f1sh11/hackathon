using System.Text.Json.Nodes;
namespace SydneyBuddy.Api.Tests;
public sealed class PoiCategoryTests {
    static JsonObject Point(string id,string kind,string broad="food")=>new(){["id"]=id,["name"]="Test "+kind,["poiKind"]=kind,["searchCategory"]=broad,["lat"]=-33.865,["lng"]=151.212,["source"]="OpenStreetMap"};
    [Theory]
    [InlineData("restaurants","japanese_restaurant")][InlineData("cafes","coffee_shop")][InlineData("bakeries","bakery")]
    [InlineData("bars","pub")][InlineData("museums","history_museum")][InlineData("parks","botanical_garden")]
    [InlineData("hotels","hotel")][InlineData("hostels","hostel")]
    [InlineData("beaches","beach")][InlineData("supermarkets","supermarket")][InlineData("toilets","toilets")][InlineData("drinking_water","drinking_water")]
    public void DetailedCategoriesMatchSourceTypesRatherThanNames(string category,string kind){
        Assert.True(PoiCategories.Valid(category));Assert.True(PoiCategories.Matches(Point("1",kind),category));
        var unrelated=Point("2","attraction");unrelated["name"]=kind;Assert.False(PoiCategories.Matches(unrelated,category));
    }
    [Fact] public void CafesDoNotIncludeRestaurantsOrRetailCoffeeShopsAndParentsIncludeChildren(){
        Assert.False(PoiCategories.Matches(Point("1","restaurant"),"cafes"));
        Assert.False(PoiCategories.Matches(Point("2","coffee","shops"),"cafes"));
        Assert.True(PoiCategories.Matches(Point("3","bakery","shops"),"food"));
        Assert.False(PoiCategories.Valid("arbitrary-category"));
    }
    [Fact] public void UnifiedAndSnapshotFilteringHappensBeforeResultLimit(){
        var points=Enumerable.Range(0,100).Select(i=>Point("c"+i,"cafe")).Append(Point("restaurant","italian_restaurant")).ToList();
        var merged=UnifiedPoiService.Merge(points,-33.865,151.212,1500,"restaurants","",default);
        Assert.Equal("restaurant",Assert.Single(merged.Array("places")!)!.Str("id"));
        var snapshot=new JsonObject{["bbox"]=new JsonArray(151.18,-33.9,151.25,-33.82),["places"]=new JsonArray(points.Select(p=>(JsonNode)p).ToArray())};
        Assert.Single(OverturePoiProvider.SearchSnapshot(snapshot,-33.865,151.212,1500,"restaurants","",default));
    }
    [Fact] public void OsmSubtypeQueriesUseActualTags(){
        Assert.Contains("[amenity=cafe]",OsmPoiProvider.Query(-33.865,151.212,1500,"cafes",""));
        Assert.Contains("[amenity=toilets]",OsmPoiProvider.Query(-33.865,151.212,1500,"toilets",""));
        Assert.Contains("[natural=beach]",OsmPoiProvider.Query(-33.865,151.212,1500,"beaches",""));
    }
}
