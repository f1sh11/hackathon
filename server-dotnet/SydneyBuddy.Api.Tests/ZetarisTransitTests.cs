using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
namespace SydneyBuddy.Api.Tests;

public sealed class ZetarisTransitTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"zt-transit-"+Guid.NewGuid());
    sealed class Warehouse:ITravelWarehouse {
        public Task<TravelSnapshot> ReadAsync(CancellationToken token)=>throw new NotImplementedException();
        public Task<JsonArray> TransitRoutesAsync(CancellationToken token)=>Task.FromResult(JsonNode.Parse(File.ReadAllText(Path.Combine(TestSupport.RepositoryRoot(),"shared","sydney-transit-routes.json")))!.AsObject().Array("routes")!);
    }
    sealed class Offline:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>throw new Exception("No network expected");}
    TripTransportRouter Router(string mode){
        Directory.CreateDirectory(root);var secrets=new SecretStore(new ProjectPaths(root));var c=new HttpClient(new Offline());
        var ctx=new DefaultHttpContext();ctx.Request.Headers[TravelDataContext.Header]=mode;
        return new(new NswJourneyProvider(c,secrets,new NavigationService(c,new ConfigurationBuilder().Build())),secrets,new TravelDataContext(new HttpContextAccessor{HttpContext=ctx}),new Warehouse());
    }
    static JsonObject Point(string name,double lat,double lng)=>new(){["name"]=name,["lat"]=lat,["lng"]=lng};
    static JsonObject Walk()=>new(){["meters"]=9000,["minutes"]=120,["source"]=JsonValue.Create("Apple Maps / MapKit"),["polyline"]=""};
    static JsonObject Profile()=>new(){["date"]="2026-10-23",["transportPreference"]="balanced",["maxSteps"]=8000};
    [Theory][InlineData("zetaris","Transport for NSW / Zetaris")][InlineData("zetaris-cloud","Transport for NSW / Zetaris Cloud")]
    public async Task WarehouseRoutesReplaceMissingTfnswKey(string mode,string source){
        var leg=await Router(mode).ChooseAsync(Point("Opera House",-33.8568,151.2153),Point("Bondi Beach",-33.8908,151.2743),Walk(),Profile(),600,new("demo",new Dictionary<string,string>()),default);
        Assert.Equal("transit",leg.Str("mode"));Assert.Equal(source,leg.Str("source"));Assert.Contains(leg.Array("steps")!.OfType<JsonObject>(),s=>s.Str("mode")=="bus");
    }
    [Fact]public async Task DirectModeWithoutKeyStillRefusesTransit(){
        await Assert.ThrowsAsync<ServiceException>(()=>Router("direct").ChooseAsync(Point("A",-33.8568,151.2153),Point("B",-33.8908,151.2743),Walk(),Profile(),600,new("demo",new Dictionary<string,string>()),default));
    }
    [Fact]public async Task ClosedServiceFallsBackToVerifiedWalking(){
        var leg=await Router("zetaris").ChooseAsync(Point("A",-33.8568,151.2153),Point("B",-33.8908,151.2743),Walk(),Profile(),180,new("demo",new Dictionary<string,string>()),default);
        Assert.Equal("Apple Maps / MapKit",leg.Str("source"));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
