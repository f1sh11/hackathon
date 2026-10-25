using System.Text.Json.Nodes;
namespace SydneyBuddy.Api.Tests;
public sealed class UnifiedPoiTests {
    static JsonObject Point(string id,string name,string kind="sights",double lat=-33.865,string source="OpenStreetMap")=>new(){["id"]=id,["name"]=name,["lat"]=lat,["lng"]=151.212,["searchCategory"]=kind,["category"]=kind=="food"?"美食":kind=="lodging"?"住宿":"風景",["source"]=source};
    [Fact] public void SameProviderBranchesWithDifferentIdsRemainSeparateEvenWhenNearby(){
        var a=Point("osm:node:1","Cafe","food");var b=Point("osm:node:2","Cafe","food",lat:-33.8651);
        Assert.Equal(2,UnifiedPoiService.Merge([a,b],-33.865,151.212,500,"food","",default).Array("places")!.Count);
    }
    [Fact] public void SameNearbyPlaceMergesSourcesAndKeepsCommunityGuidePhotos(){
        var a=Point("osm:node:1","Public Reserve");var b=Point("overture:test","Public Reserve",source:"Overture Maps");var c=Point("gem:test","Public Reserve",source:"Wikivoyage");c["guide"]=new JsonObject{["directions"]="Public path"};c["photos"]=new JsonArray(new JsonObject{["license"]="CC BY 4.0"});
        var result=UnifiedPoiService.Merge([a,b,c],-33.865,151.212,1500,"all","Reserve",default).Array("places")!;
        var one=Assert.Single(result)!.AsObject();Assert.Equal("gem:test",one.Str("id"));Assert.Equal(3,one.Array("sources")!.Count);Assert.Equal("Public path",one.Object("guide")!.Str("directions"));Assert.Single(one.Array("photos")!);Assert.Null(one["searchCategory"]);
    }
    [Fact] public void BranchesWithSameNameFarApartStaySeparateAndRadiusIsEnforced(){
        var a=Point("osm:node:1","Coffee",kind:"food");var b=Point("osm:node:2","Coffee",kind:"food",lat:-33.866);var far=Point("osm:node:3","Coffee",kind:"food",lat:-34);
        Assert.Equal(2,UnifiedPoiService.Merge([a,b,far],-33.865,151.212,1000,"food","coffee",default).Array("places")!.Count);
    }
    [Fact] public void AllCategoryQueryReservesRoomForHotelsAndCommunityPlaces(){
        var points=Enumerable.Range(0,150).Select(i=>Point("osm:node:"+i,"Cafe "+i,"food")).ToList();points.Add(Point("osm:node:200","Hotel","lodging"));points.Add(Point("gem:test","Garden",source:"Wikivoyage"));
        var result=UnifiedPoiService.Merge(points,-33.865,151.212,5000,"all","",default);var found=result.Array("places")!;
        Assert.True(result.Bool("truncated"));Assert.Equal(152,result.Int("totalMatches"));Assert.Contains(found,p=>p!.Str("name")=="Hotel");Assert.Contains(found,p=>p!.Str("name")=="Garden");Assert.True(found.Count<=80);
    }
    [Fact] public void LodgingSearchAndToolboxKeepCategoryAndProvenance(){
        var hotel=Point("osm:node:201","Test Hotel","lodging");hotel["sourceUrl"]="https://www.openstreetmap.org/node/201";var cafe=Point("osm:node:202","Hotel Cafe","food");
        var result=UnifiedPoiService.Merge([hotel,cafe],-33.865,151.212,1500,"lodging","Hotel",default);var selected=Assert.Single(result.Array("places")!)!.AsObject();var imported=PoiService.Import(selected);Assert.Equal("住宿",imported.Str("category"));Assert.Equal(hotel.Str("sourceUrl"),imported.Str("sourceUrl"));Assert.Single(imported.Array("sources")!);
    }
}
