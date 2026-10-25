using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
namespace SydneyBuddy.Api.Tests;

public sealed class SocialEvidenceTests {
    public static JsonObject Input()=>JsonNode.Parse("""{"platform":"instagram","postUrl":"https://www.instagram.com/p/local-test/?tracking=1","text":"Test Cafe is a hidden gem. Try mushroom gnocchi for AUD 40 per person. Walk from the station. #Sydney","mentions":[{"name":"Test Cafe","kind":"restaurant","quote":"Test Cafe is a hidden gem.","hiddenGemQuote":"hidden gem","dishes":[{"name":"mushroom gnocchi","quote":"Try mushroom gnocchi for AUD 40 per person."}],"prices":[{"quote":"AUD 40 per person"}],"reviews":[{"quote":"Test Cafe is a hidden gem."}],"accessQuote":"Walk from the station."}]}""")!.AsObject();
    [Fact] public void QuotesMustExistAndNeverBecomeRatingsOrAveragePrices(){
        var input=Input();var mentions=SocialImportService.ValidateMentions(input);Assert.Single(mentions);
        Assert.Null(mentions[0]!["rating"]);Assert.Null(mentions[0]!["cost"]);
        input.Array("mentions")![0]!["quote"]="Invented recommendation";
        Assert.Throws<ServiceException>(()=>SocialImportService.ValidateMentions(input));
    }
    [Fact] public async Task AiOutputRequiresEvidenceAndCannotPublishAnything(){
        var root=Path.Combine(Path.GetTempPath(),"social-ai-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try{
            await File.WriteAllTextAsync(Path.Combine(root,".secrets.json"),"{\"NVIDIA_API_KEY\":\"test-only-key\"}");
            var fake=new FakeExternal();var input=Input();
            fake.Respond=_=>new JsonObject{["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["content"]=new JsonObject{["mentions"]=input["mentions"]!.DeepClone()}.ToJsonString()}})};
            var service=new SocialImportService(null!,null!,fake,new SecretStore(new ProjectPaths(root)));
            var result=await service.ExtractAsync(input,default);Assert.Equal("draft",result.Str("status"));Assert.Single(result.Array("mentions")!);Assert.Single(fake.Calls);
            input.Array("mentions")![0]!["quote"]="Invented meal and place";
            await Assert.ThrowsAsync<ServiceException>(()=>service.ExtractAsync(input,default));
        }finally{Directory.Delete(root,true);}
    }
    [Fact] public void ZetarisSearchReadsSocialRecordsFromItsSnapshot(){
        var point=new JsonObject{["id"]="social:"+new string('a',24)+":0",["name"]="Test Cafe",["category"]="美食",["poiKind"]="cafe",["lat"]=-33.865,["lng"]=151.212,["source"]="Community social recommendation",["socialRecommendations"]=new JsonArray(new JsonObject{["quote"]="Test Cafe"})};
        var context=new Microsoft.AspNetCore.Http.DefaultHttpContext();context.Request.Headers[TravelDataContext.Header]="zetaris";
        context.Items[typeof(TravelSnapshot)]=new TravelSnapshot(new JsonObject{["places"]=new JsonArray()},new(),new(),"test",new(),new(),new JsonArray(point));
        var travel=new TravelDataContext(new Microsoft.AspNetCore.Http.HttpContextAccessor{HttpContext=context});
        var service=new UnifiedPoiService(travel,null!,null!,null!);
        var result=service.Search(-33.865,151.212,500,"food","Test",default);Assert.Equal("zetaris",result.Str("dataSource"));Assert.Single(result.Array("places")!);
    }
    [Theory][InlineData("https://instagram.com.evil.test/p/a/")][InlineData("http://www.instagram.com/p/a/")][InlineData("https://user:password@www.instagram.com/p/a/")]
    public void RejectsInvalidPlatformUrls(string url)=>Assert.Throws<ServiceException>(()=>SocialImportService.PlatformUrl("instagram",url));
    [Fact] public void StripsTrackingAndRejectsUnlicensedPhotos(){
        Assert.Equal("https://www.instagram.com/p/local-test",SocialImportService.PlatformUrl("instagram",Input().Str("postUrl")));
        var input=Input();input["photos"]=new JsonArray(new JsonObject{["url"]="https://example.com/image.jpg"});
        Assert.Throws<ServiceException>(()=>SocialImportService.Photos(input));
    }
}
public sealed class SocialImportApiTests(ApiFixture fixture):IClassFixture<ApiFixture> {
    [Fact] public async Task ImportPublishSearchAndToolboxKeepEvidenceAndAreIdempotent(){
        var root=fixture.TemporaryRoot;
        Directory.CreateDirectory(Path.Combine(root,"infra","poi","data"));Directory.CreateDirectory(Path.Combine(root,"infra","zetaris","data"));
        await File.WriteAllTextAsync(Path.Combine(root,"infra","poi","data","places.json"),"""{"schemaVersion":1,"source":"Overture Maps","bbox":[151.1,-33.95,151.3,-33.8],"places":[]}""");
        await File.WriteAllTextAsync(Path.Combine(root,"infra","zetaris","data","hidden-gems.json"),"{\"places\":[]}");
        await File.WriteAllTextAsync(Path.Combine(root,"infra","zetaris","data","osm-map-places.json"),"""{"places":[{"id":"osm:node:123","name":"Test Cafe","category":"美食","searchCategory":"food","poiKind":"cafe","lat":-33.865,"lng":151.212,"source":"OpenStreetMap","description":"Map description"}]}""");
        async Task<JsonObject> Post(string path,JsonObject input){var response=await fixture.Client.PostAsJsonAsync(path,input);response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<JsonObject>())!;}
        var record=await Post("/social/import",SocialEvidenceTests.Input());var id=record.Str("id");Assert.Equal("draft",record.Str("status"));
        Assert.Equal(id,(await Post("/social/import",SocialEvidenceTests.Input())).Str("id"));
        var invalid=SocialEvidenceTests.Input();invalid.Array("mentions")![0]!["quote"]="made up";
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await fixture.Client.PostAsJsonAsync("/social/import",invalid)).StatusCode);
        var search=new JsonObject{["lat"]=-33.865,["lng"]=151.212,["radius"]=500,["category"]="cafes",["query"]="Test Cafe",["provider"]="osm"};
        var before=await Post("/poi/search",search);Assert.Single(before.Array("places")!);Assert.False(before.Array("places")![0]!.Str("id").StartsWith("social:"));
        var published=await Post("/social/"+id+"/publish",new(){["mentionIndex"]=0,["placeId"]="osm:node:123",["placeName"]="Test Cafe",["lat"]=-33.865,["lng"]=151.212});
        Assert.Single(published.Array("places")!);
        var after=await Post("/poi/search",search);var place=Assert.Single(after.Array("places")!)!.AsObject();Assert.StartsWith("social:",place.Str("id"));Assert.Contains("mushroom gnocchi",place.Str("description"));Assert.Single(place.Array("socialRecommendations")!);
        var imported=PoiService.Import(place);Assert.Equal(place.Str("id"),imported.Str("id"));Assert.Single(imported.Array("socialRecommendations")!);
        await using var factory=fixture.CreateFactory();using var scope=factory.Services.CreateScope();var persisted=scope.ServiceProvider.GetRequiredService<SocialImportStore>();Assert.Single(persisted.Published());
    }
}
