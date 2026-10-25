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
    [Fact] public void FoodPoisReceiveDeterministicPriceTierMapping(){
        var cafe=Point("c1","cafe");cafe["category"]="美食";PoiCategories.ApplyFoodPriceMapping(cafe);
        Assert.Equal(1,cafe.Int("priceRange"));Assert.Equal("$",cafe.Str("priceTier"));Assert.Equal(20,cafe.Int("estimatedSpendAud"));Assert.Equal("$ · 預估均消 A$20/人",cafe.Str("priceLabel"));
        var thai=Point("r1","thai_restaurant");thai["category"]="美食";PoiCategories.ApplyFoodPriceMapping(thai);
        Assert.Equal(2,thai.Int("priceRange"));Assert.Equal("$$",thai.Str("priceTier"));Assert.Equal(40,thai.Int("estimatedSpendAud"));
        var steak=Point("s1","steakhouse");steak["category"]="美食";PoiCategories.ApplyFoodPriceMapping(steak);
        Assert.Equal(3,steak.Int("priceRange"));Assert.Equal("$$$",steak.Str("priceTier"));Assert.Equal(75,steak.Int("estimatedSpendAud"));
    }
    [Fact] public void LodgingPoisReceiveDeterministicPriceTierMapping(){
        var hostel=Point("l1","hostel","lodging");hostel["category"]="住宿";PoiCategories.ApplyFoodPriceMapping(hostel);
        Assert.Equal(1,hostel.Int("priceRange"));Assert.Equal("$",hostel.Str("priceTier"));Assert.Equal(65,hostel.Int("nightlyRateAud"));Assert.Equal("$ · 預估每晚 A$65/晚",hostel.Str("priceLabel"));
        var hotel=Point("l2","hotel","lodging");hotel["category"]="住宿";PoiCategories.ApplyFoodPriceMapping(hotel);
        Assert.Equal(2,hotel.Int("priceRange"));Assert.Equal("$$",hotel.Str("priceTier"));Assert.Equal(185,hotel.Int("nightlyRateAud"));
        var suites=Point("l3","hotel","lodging");suites["category"]="住宿";suites["name"]="Meriton Suites Pitt Street";PoiCategories.ApplyFoodPriceMapping(suites);
        Assert.Equal(3,suites.Int("priceRange"));Assert.Equal("$$$",suites.Str("priceTier"));Assert.Equal(295,suites.Int("nightlyRateAud"));
        var luxury=Point("l4","hotel","lodging");luxury["category"]="住宿";luxury["name"]="Park Hyatt Sydney";PoiCategories.ApplyFoodPriceMapping(luxury);
        Assert.Equal(4,luxury.Int("priceRange"));Assert.Equal("$$$$",luxury.Str("priceTier"));Assert.Equal(620,luxury.Int("nightlyRateAud"));
        Assert.StartsWith("★ ",luxury.Str("ratingLabel"));Assert.True(luxury.Number("aggregateRating")>=4.5);Assert.True(luxury.Int("reviewCount")>=650);
        var sight=Point("s10","museum","sights");sight["category"]="風景";sight["name"]="Australian Museum";PoiCategories.ApplyFoodPriceMapping(sight);
        Assert.StartsWith("★ ",sight.Str("ratingLabel"));Assert.NotEmpty(sight.Str("reviewSummary"));Assert.NotEmpty(sight.Array("reviewHighlights")!);
    }
}
