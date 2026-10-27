using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api.Tests;

[Trait("Category","Integration")]
public sealed class PlanningTestApiTests(ApiFixture fixture):IClassFixture<ApiFixture>
{
    [Fact] public async Task JsonBodyProducesDraftWithoutSavingATrip()
    {
        var before=await fixture.Client.GetFromJsonAsync<JsonArray>("/trips");
        var response=await fixture.Client.PostAsJsonAsync("/planning/test",TestSupport.Profile());
        response.EnsureSuccessStatusCode();var result=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.False(result.Bool("saved"));Assert.False(string.IsNullOrWhiteSpace(result.Str("runId")));Assert.Equal(ItinerarySkill.Version,result.Str("skillVersion"));
        Assert.NotEmpty(result.Object("draft")!.Array("days")!);
        Assert.Equal(before!.Count,(await fixture.Client.GetFromJsonAsync<JsonArray>("/trips"))!.Count);
    }
    [Fact] public async Task UploadedJsonCanDownloadTheSamePlanningResultContract()
    {
        using var form=new MultipartFormDataContent();
        form.Add(new StringContent(TestSupport.Profile().ToJsonString(),Encoding.UTF8,"application/json"),"file","request.json");
        var response=await fixture.Client.PostAsync("/planning/test?download=true",form);response.EnsureSuccessStatusCode();
        Assert.Equal("attachment",response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("planning-result.json",response.Content.Headers.ContentDisposition.ToString());
        Assert.NotNull((await response.Content.ReadFromJsonAsync<JsonObject>())!.Object("draft"));
    }
    [Fact] public async Task ExampleIsAJsonRequestNotASavedTripOrSecretConfiguration()
    {
        var response=await fixture.Client.GetAsync("/planning/test/example");response.EnsureSuccessStatusCode();
        var example=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("demo",example.Str("mode"));Assert.Equal(1,example.Int("days"));
        Assert.Equal("walk",example.Str("transportPreference"));Assert.False(example.ContainsKey("accessCode"));
        Assert.NotNull(PlanValidator.Validate(example,new PlaceCatalog(new CatalogCache(),new(TestSupport.RepositoryRoot())).Get()));
    }
    [Theory][InlineData("[]")][InlineData("{")][InlineData("null")]
    public async Task NonObjectOrMalformedJsonIsRejected(string input)
    {
        var r=await fixture.Client.PostAsync("/planning/test",new StringContent(input,Encoding.UTF8,"application/json"));
        Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
    }
    [Fact] public async Task UploadMustContainExactlyOneNamedJsonFile()
    {
        using var form=new MultipartFormDataContent();form.Add(new StringContent("{}"),"wrong","request.json");
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await fixture.Client.PostAsync("/planning/test",form)).StatusCode);
        using var other=new MultipartFormDataContent();other.Add(new StringContent("{}"),"file","request.txt");
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await fixture.Client.PostAsync("/planning/test",other)).StatusCode);
    }
    [Fact] public async Task SizeAndContentTypeAreBoundedBeforePlanning()
    {
        var r=await fixture.Client.PostAsync("/planning/test",new StringContent(new string(' ',PlanningTestEndpoints.MaxFileBytes+1),Encoding.UTF8,"application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,r.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,(await fixture.Client.PostAsync("/planning/test",new StringContent("{}"))).StatusCode);
    }
    [Fact] public async Task FileTestCannotBypassPaidProviderPolicy()
    {
        var input=TestSupport.Profile();input["mode"]="live";
        var normal=await fixture.Client.PostAsJsonAsync("/plan",input);
        var test=await fixture.Client.PostAsJsonAsync("/planning/test",input);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,normal.StatusCode);Assert.Equal(normal.StatusCode,test.StatusCode);
    }
    [Fact]public async Task EffectiveSkillIsDownloadableButCannotBeChangedWithAnAppRequest()
    {
        var response=await fixture.Client.GetAsync("/planning/skills?language=en&download=true");response.EnsureSuccessStatusCode();
        Assert.Equal("text/markdown",response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("physical attraction uniqueness",await response.Content.ReadAsStringAsync());
        var metadata=(await fixture.Client.GetFromJsonAsync<JsonObject>("/planning/skills?language=zh-Hant"))!;
        Assert.Equal(ItinerarySkill.BaseVersion,metadata.Str("version"));Assert.Equal(64,metadata.Str("sha256").Length);
        Assert.False((await fixture.Client.PutAsJsonAsync("/planning/skills",new {content="replace"})).IsSuccessStatusCode);
    }
    sealed class TestWarehouse:ITravelWarehouse {
        public int Reads;
        public Task<TravelSnapshot> ReadAsync(CancellationToken ct){
            Reads++;var catalog=JsonNode.Parse(File.ReadAllText(Path.Combine(TestSupport.RepositoryRoot(),"server","places.json")))!.AsArray();
            return Task.FromResult(new TravelSnapshot(new JsonObject{["places"]=new JsonArray()},catalog,new JsonArray(),DateTimeOffset.UtcNow.ToString("O")));
        }
        public Task<JsonObject> WeatherRawAsync(CancellationToken ct)=>Task.FromResult(JsonNode.Parse("""{"timezone":"Australia/Sydney","daily":{"time":["2026-10-24"],"temperature_2m_min":[15],"temperature_2m_max":[25],"precipitation_probability_max":[0],"weather_code":[0]}}""")!.AsObject());
    }
    [Fact]public async Task ManagedCloudLoadsWarehouseForRawAndUploadedPlanningButNotExampleDownloads()
    {
        var warehouse=new TestWarehouse();
        await using var host=fixture.CreateFactory().WithWebHostBuilder(web=>{
            web.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?>{["TravelData:ManagedSource"]="zetaris-cloud"}));
            web.ConfigureServices(s=>{s.RemoveAll<ITravelWarehouse>();s.AddSingleton<ITravelWarehouse>(warehouse);});
        });
        using var client=host.CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        (await client.GetAsync("/planning/test/example")).EnsureSuccessStatusCode();Assert.Equal(0,warehouse.Reads);
        var raw=await client.PostAsJsonAsync("/planning/test",TestSupport.Profile());raw.EnsureSuccessStatusCode();
        using var form=new MultipartFormDataContent();form.Add(new StringContent(TestSupport.Profile().ToJsonString(),Encoding.UTF8,"application/json"),"file","request.json");
        (await client.PostAsync("/planning/test?download=true",form)).EnsureSuccessStatusCode();Assert.Equal(2,warehouse.Reads);
    }
}
