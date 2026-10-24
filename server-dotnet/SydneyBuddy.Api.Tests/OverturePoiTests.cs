using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
namespace SydneyBuddy.Api.Tests;

public sealed class OverturePoiTests:IDisposable {
    readonly string root=Path.Combine(Path.GetTempPath(),"buddy-overture-test-"+Guid.NewGuid().ToString("N"));
    const string Id="overture:00000000-0000-4000-8000-000000000001";
    string FileName=>Path.Combine(root,"snapshot.json");
    OverturePoiProvider Provider()=>new(new ProjectPaths(root),new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Poi:OvertureFile"]=FileName}).Build());
    JsonObject Point(string id,string name,double lat=-33.86,double lng=151.21,string category="food")=>new(){["id"]=id,["name"]=name,["lat"]=lat,["lng"]=lng,["source"]="Overture Maps",["searchCategory"]=category,["searchText"]=name+" café Sydney",["poiKind"]="cafe",["category"]="美食",["visitMinutes"]=45,["cost"]=0,["website"]="https://example.com",["sourceRelease"]="2026-10-21.0"};
    void Save(params JsonObject[] points){Directory.CreateDirectory(root);File.WriteAllText(FileName,new JsonObject{["schemaVersion"]=1,["source"]="Overture Maps",["release"]="2026-10-21.0",["downloadedAt"]="2026-10-24T00:00:00Z",["bbox"]=new JsonArray(151.18,-33.90,151.24,-33.84),["places"]=new JsonArray(points.Select(p=>(JsonNode)p).ToArray())}.ToJsonString());}
    [Fact]public async Task FiltersNameCategoryAndActualRadiusAndSeparatesSourceFromMap(){
        Save(Point(Id,"Café Test"),Point("overture:00000000-0000-4000-8000-000000000002","Far Cafe",-33.89,151.23),Point("overture:00000000-0000-4000-8000-000000000003","Museum",category:"sights"));
        using var cache=new MemoryCache(new MemoryCacheOptions());var service=new PoiService(Provider(),cache);
        var input=new JsonObject{["lat"]=-33.86,["lng"]=151.21,["radius"]=1500,["category"]="food",["query"]="cafe"};
        var result=await service.SearchAsync(input,default);Assert.Single(result.Array("places")!);Assert.Equal(Id,result.Array("places")![0]!.Str("id"));Assert.Null(result.Array("places")![0]!["searchText"]);Assert.Equal("Overture Maps / local snapshot",result.Str("source"));Assert.Equal("2026-10-21.0",result.Object("metadata")!.Str("release"));
        await service.SearchAsync(input,default);input["category"]="sights";input["query"]="Museum";Assert.Single((await service.SearchAsync(input,default)).Array("places")!); // local searches have no remote cooldown.
        input["lat"]=-33.81;await Assert.ThrowsAsync<ServiceException>(()=>service.SearchAsync(input,default));
    }
    [Fact]public async Task MissingInvalidAndCancelledRequestsNeverProduceFakeResults(){
        var provider=Provider();await Assert.ThrowsAsync<ServiceException>(()=>provider.SearchAsync(-33.86,151.21,1500,"food","",default));
        Directory.CreateDirectory(root);File.WriteAllText(FileName,"not json");await Assert.ThrowsAsync<ServiceException>(()=>provider.SearchAsync(-33.86,151.21,1500,"food","",default));
        using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(()=>provider.SearchAsync(-33.86,151.21,1500,"food","",cts.Token));
    }
    [Fact]public async Task AtomicSnapshotReplacementIsReloadedAndImportedFieldsRemainUnknown(){
        Save(Point(Id,"Before"));var provider=Provider();Assert.Single(await provider.SearchAsync(-33.86,151.21,1500,"food","Before",default));
        Save(Point(Id,"After"));File.SetLastWriteTimeUtc(FileName,DateTime.UtcNow.AddSeconds(2));Assert.Single(await provider.SearchAsync(-33.86,151.21,1500,"food","After",default));
        var point=Point(Id,"Cafe");point["cost"]=500;point["openMinute"]=0;point["closeMinute"]=1440;
        var imported=PoiService.Import(point);Assert.Equal("Overture Maps",imported.Str("source"));Assert.Equal(0,imported.Int("cost"));Assert.Null(imported["openMinute"]);Assert.Equal("https://example.com",imported.Str("website"));Assert.Equal("meal",PoiService.ActivityKind(imported));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
