using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
namespace SydneyBuddy.Api.Tests;
public sealed class GooglePoiTests {
    [Fact]public async Task GoogleUsesNativeSearchContractsWithoutOsmCacheAndPaidPolicyBlocksCalls(){
        var root=Path.Combine(Path.GetTempPath(),"buddy-google-poi-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            File.WriteAllText(Path.Combine(root,".secrets.json"),"{\"GOOGLE_SERVER_KEY\":\"synthetic-test-key\"}");
            var secrets=new SecretStore(new ProjectPaths(root));
            var external=new FakeExternal{Respond=_=>JsonNode.Parse("""{"places":[{"id":"test-place","displayName":{"text":"Test Cafe"},"location":{"latitude":-33.86,"longitude":151.21},"primaryType":"cafe","attributions":[{"provider":"Test data provider"}]},{"id":"outside","displayName":{"text":"Outside"},"location":{"latitude":-33.7,"longitude":151.21}}]}""")!.AsObject()};
            PaidServicePolicy Policy(bool allowed)=>new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["AllowPaidServices"]=allowed.ToString()}).Build());
            var denied=new GooglePoiProvider(external,secrets,Policy(false));
            await Assert.ThrowsAsync<ServiceException>(()=>denied.SearchResultAsync(-33.86,151.21,1500,"food","","en",default));Assert.Empty(external.Calls);
            var provider=new GooglePoiProvider(external,secrets,Policy(true));using var cache=new MemoryCache(new MemoryCacheOptions());var service=new PoiService(new OsmFake(),cache,provider);
            var input=new JsonObject{["provider"]="google",["lat"]=-33.86,["lng"]=151.21,["category"]="food",["language"]="zh-TW"};
            var result=await service.SearchAsync(input,default);Assert.Single(result.Array("places")!);Assert.Equal("Google Places",result.Str("source"));Assert.Contains("Test data provider",result.Str("attribution"));
            var call=external.Calls.Single();Assert.EndsWith("places:searchNearby",call.Url);Assert.Contains("restaurant",call.Payload!.Array("includedTypes")!.Select(x=>x!.ToString()));Assert.Equal(1500,call.Payload.Object("locationRestriction")!.Object("circle")!.Int("radius"));Assert.True(call.Headers.ContainsKey("X-Goog-FieldMask"));Assert.Equal("en",call.Payload.Str("languageCode"));
            await service.SearchAsync(input,default);Assert.Equal(2,external.Calls.Count); // Google is never served from the OSM cache.
            input["category"]="cafes";var cafes=await service.SearchAsync(input,default);
            Assert.Equal("美食",Assert.Single(cafes.Array("places")!)!.Str("category"));
            Assert.Contains("cafe",external.Calls.Last().Payload!.Array("includedTypes")!.Select(x=>x!.ToString()));
            var count=external.Calls.Count;input["category"]="drinking_water";
            var unavailable=await service.SearchAsync(input,default);Assert.Empty(unavailable.Array("places")!);Assert.Contains("drinking-water",unavailable.Str("message"));Assert.Equal(count,external.Calls.Count);
            input["category"]="food";
            input["query"]="Cafe";await service.SearchAsync(input,default);Assert.EndsWith("places:searchText",external.Calls.Last().Url);Assert.Equal("Cafe",external.Calls.Last().Payload!.Str("textQuery"));Assert.Null(external.Calls.Last().Payload!["includedType"]);
        }finally{Directory.Delete(root,true);}
    }
    sealed class OsmFake:IPoiProvider {public Task<JsonArray> SearchAsync(double a,double b,int r,string c,string q,CancellationToken token)=>throw new Exception("Google query reached OSM");}
}
