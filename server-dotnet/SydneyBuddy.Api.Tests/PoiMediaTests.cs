using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
namespace SydneyBuddy.Api.Tests;
public class PoiMediaTests {
    sealed class Handler(string payload):HttpMessageHandler {
        public int Calls;public Uri? Url;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;Url=request.RequestUri;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(payload)});}
    }
    [Fact] public async Task NearbyImagesHaveCreditsSafeHostsAndCachedIndependentResults(){
        var handler=new Handler("""{"query":{"pages":{"1":{"title":"File:Nearby.jpg","imageinfo":[{"mime":"image/jpeg","thumburl":"https://upload.wikimedia.org/photo.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Nearby.jpg","extmetadata":{"Artist":{"value":"<a>Photographer</a>"},"LicenseShortName":{"value":"CC BY-SA 4.0"}}}]},"2":{"imageinfo":[{"mime":"image/jpeg","thumburl":"https://other.example/photo.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Wrong.jpg"}]}}}}""");
        using var cache=new MemoryCache(new MemoryCacheOptions());var media=new PoiMediaService(new HttpClient(handler),cache);
        var point=new JsonObject{["lat"]=-33.865,["lng"]=151.212};var result=await media.LoadAsync(point,default);
        Assert.Single(result.Array("photos")!);Assert.Equal("Photographer",result.Array("photos")![0]!.Str("artist"));Assert.Equal(100,result.Int("nearbyRadiusMeters"));
        result.Array("photos")!.Clear();Assert.Single((await media.LoadAsync(point,default)).Array("photos")!);Assert.Equal(1,handler.Calls);Assert.Equal("commons.wikimedia.org",handler.Url!.Host);
        await Assert.ThrowsAsync<ServiceException>(()=>media.LoadAsync(new JsonObject(),default));Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task NoCoverageReturnsEmptyAndProviderErrorsAreNotFakePhotos(){
        using var cache=new MemoryCache(new MemoryCacheOptions());var point=new JsonObject{["lat"]=-33.86,["lng"]=151.21};
        var media=new PoiMediaService(new HttpClient(new Handler("{}")),cache);Assert.Empty((await media.LoadAsync(point,default)).Array("photos")!);
        using var other=new MemoryCache(new MemoryCacheOptions());media=new PoiMediaService(new HttpClient(new Handler("{\"error\":{\"code\":\"ratelimited\"}}")),other);
        await Assert.ThrowsAsync<ServiceException>(()=>media.LoadAsync(point,default));
    }
    [Fact] public async Task PlaceNameMatchesExactTitleAndFiltersNoise(){
        var payload="""{"query":{"pages":{"1":{"title":"File:Sticker art in Sydney 01.jpg","imageinfo":[{"mime":"image/jpeg","thumburl":"https://upload.wikimedia.org/sticker.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Sticker.jpg","extmetadata":{"Artist":{"value":"A"},"LicenseShortName":{"value":"CC BY 4.0"}}}]},"2":{"title":"File:Susannah Place Museum exterior.jpg","imageinfo":[{"mime":"image/jpeg","thumburl":"https://upload.wikimedia.org/susannah.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Susannah.jpg","extmetadata":{"Artist":{"value":"Curator"},"LicenseShortName":{"value":"CC BY-SA 4.0"}}}]},"3":{"title":"File:Sydney Mrs Macquarie's Chair sunset.jpg","imageinfo":[{"mime":"image/jpeg","thumburl":"https://upload.wikimedia.org/chair.jpg","descriptionurl":"https://commons.wikimedia.org/wiki/File:Chair.jpg","extmetadata":{"Artist":{"value":"Harbour Photog"},"LicenseShortName":{"value":"CC BY 4.0"}}}]}}}}""";
        using var cache=new MemoryCache(new MemoryCacheOptions());var media=new PoiMediaService(new HttpClient(new Handler(payload)),cache);
        var susannah=await media.LoadAsync(new JsonObject{["name"]="Susannah Place Museum",["lat"]=-33.8592,["lng"]=151.2067},default);
        Assert.Equal("Susannah Place Museum exterior.jpg",susannah.Array("photos")![0]!.Str("title"));
        Assert.Equal("exact-place",susannah.Array("photos")![0]!.Str("matchKind"));
        Assert.DoesNotContain(susannah.Array("photos")!.OfType<JsonObject>(),p=>p.Str("title").Contains("Sticker art"));
        var chair=await media.LoadAsync(new JsonObject{["name"]="麥格理夫人椅",["lat"]=-33.8597,["lng"]=151.2225},default);
        Assert.Equal("Sydney Mrs Macquarie's Chair sunset.jpg",chair.Array("photos")![0]!.Str("title"));
        Assert.Equal("exact-place",chair.Array("photos")![0]!.Str("matchKind"));
    }
}
