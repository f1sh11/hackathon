using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace SydneyBuddy.Api.Tests;

public sealed class JourneyProviderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "buddy-provider-tests-"+Guid.NewGuid().ToString("N"));
    private readonly JourneyPoint from = new("测试起点",-33.8611,151.2126,0), to = new("测试终点",-33.8568,151.2153,0);
    private static readonly DateTimeOffset Departure = DateTimeOffset.Parse("2026-10-23T10:00:00+11:00");
    private static IConfiguration Config(bool paid=false) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["AllowPaidServices"]=paid.ToString(), ["Navigation:ValhallaUrl"]="http://127.0.0.1:8002/route" }).Build();
    private SecretStore Secrets() { Directory.CreateDirectory(root);var store=new SecretStore(new ProjectPaths(root));store.Save(new Dictionary<string,string>{{"GOOGLE_SERVER_KEY","synthetic-test-key"},{"TFNSW_API_KEY","synthetic-nsw-key"}});return store; }
    private static JsonObject Fixture(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(TestSupport.RepositoryRoot(),"tests","fixtures",name)))!.AsObject();
    private JsonObject Input(string provider,string map="osm",string content="open") => new() { ["routingProvider"]=provider,["mapProvider"]=map,["contentSource"]=content,["departureTime"]=Departure.ToString("O"),["modes"]=new JsonArray("transit"),["points"]=new JsonArray(new JsonObject{["name"]=from.Name,["lat"]=from.Lat,["lng"]=from.Lng},new JsonObject{["name"]=to.Name,["lat"]=to.Lat,["lng"]=to.Lng}) };
    [Fact] public async Task GoogleDisabledByServerPolicyNeverCallsNetwork()
    {
        var handler=new CaptureHandler(_=>throw new Exception("Unexpected network"));
        var provider=new GoogleJourneyProvider(new HttpClient(handler),Secrets(),new PaidServicePolicy(Config()));
        await Assert.ThrowsAsync<ServiceException>(()=>provider.RouteAsync(from,to,"transit",Departure,default));Assert.Equal(0,handler.Count);
    }
    [Theory][InlineData("google","osm","open")][InlineData("google","apple","google")][InlineData("open","google","google")][InlineData("unknown","osm","open")][InlineData("apple","apple","open")]
    public async Task UnsupportedOrMismatchedSourcesFailBeforeAnyNetwork(string id,string map,string content)
    {
        var handler=new CaptureHandler(_=>throw new Exception("Unexpected network"));var config=Config(true);var client=new HttpClient(handler);
        var provider=new GoogleJourneyProvider(client,Secrets(),new PaidServicePolicy(config));
        var service=new JourneyService(client,config,new NavigationService(client,config),[provider]);
        await Assert.ThrowsAsync<ServiceException>(()=>service.PlanAsync(Input(id,map,content),default));Assert.Equal(0,handler.Count);
    }
    [Fact] public async Task GoogleUsesItsOwnEndpointKeyAndTransitDetailsAndPolyline5()
    {
        var handler=new CaptureHandler(request=>{
            Assert.Equal("routes.googleapis.com",request.RequestUri!.Host);Assert.True(request.Headers.Contains("X-Goog-Api-Key"));
            Assert.Contains("transitDetails",request.Headers.GetValues("X-Goog-FieldMask").Single());return Fixture("google-journey-test.json");});
        var client=new HttpClient(handler);var config=Config(true);
        var service=new JourneyService(client,config,new NavigationService(client,config),[new GoogleJourneyProvider(client,Secrets(),new PaidServicePolicy(config))]);
        var plan=await service.PlanAsync(Input("google","google","google"),default);
        Assert.Equal("Google Routes",plan.Source);var bus=plan.Legs[0].Steps[1];
        Assert.Equal("bus",bus.Mode);Assert.Equal("TEST-01",bus.Line);Assert.Equal(4,bus.StopCount);Assert.Equal("测试上车站 A",bus.From);
        Assert.Equal("测试上车站 A",plan.Legs[0].Steps[0].To);
        Assert.Equal(300,bus.WaitSeconds);Assert.False(bus.Realtime);Assert.True(bus.Geometry.Count>10);
        Assert.Equal(-33.86108,plan.Legs[0].Steps[0].Geometry[0].Lat,5);
        Assert.Equal("TRANSIT",handler.Body!.Str("travelMode"));Assert.Null(handler.Body!["intermediates"]);
    }
    [Fact] public async Task NswUsesSydneyTimeLongitudeFirstInputAndLatitudeFirstOutput()
    {
        var handler=new CaptureHandler(request=>{
            Assert.Equal("api.transport.nsw.gov.au",request.RequestUri!.Host);Assert.StartsWith("apikey ",request.Headers.GetValues("Authorization").Single());
            var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
            Assert.Equal("151.2126:-33.8611:EPSG:4326",query["name_origin"].ToString());Assert.Equal("20261023",query["itdDate"].ToString());Assert.Equal("1000",query["itdTime"].ToString());
            return Fixture("nsw-journey-test.json");});
        var config=Config();var client=new HttpClient(handler);var service=new JourneyService(client,config,new NavigationService(client,config),[new NswJourneyProvider(client,Secrets(),new NavigationService(client,config))]);
        var plan=await service.PlanAsync(Input("nsw"),default);var bus=plan.Legs[0].Steps[1];
        Assert.Equal("TEST-01",bus.Line);Assert.False(bus.Realtime);Assert.True(bus.Geometry.Count>10);
        Assert.InRange(bus.Geometry[0].Lat,-34,-33);Assert.InRange(bus.Geometry[0].Lng,151,152);Assert.Equal(300,bus.WaitSeconds);
    }
    [Theory][InlineData("missing-shape")][InlineData("realtime")][InlineData("booking")][InlineData("bad-coordinates")][InlineData("bad-time")]
    public void NswNeverInventsShapesOrRealtimeAndRejectsUnsafeServiceData(string variant)
    {
        var fixture=Fixture("nsw-journey-test.json");var bus=fixture.Array("journeys")![0]!.Array("legs")![1]!;
        if(variant=="missing-shape")bus["coords"]=new JsonArray();
        if(variant=="realtime"){bus["isRealtimeControlled"]=true;bus["origin"]!["departureTimeEstimated"]=bus["origin"]!.Str("departureTimePlanned");}
        if(variant=="booking")bus["transportation"]!["iconId"]=23;
        if(variant=="bad-coordinates")bus["coords"]=new JsonArray(new JsonArray(151,-33));
        if(variant=="bad-time")bus["destination"]!["arrivalTimePlanned"]="2026-10-23T09:00:00+11:00";
        if(variant is "booking" or "bad-coordinates" or "bad-time")Assert.Throws<ServiceException>(()=>NswJourneyProvider.Parse(fixture,Departure));
        else {var steps=NswJourneyProvider.Parse(fixture,Departure);if(variant=="missing-shape")Assert.Empty(steps[1].Geometry);else Assert.True(steps[1].Realtime);}
    }
    [Fact] public void NswSkipsAnEarlierDepartureAndKeepsTheNextTimetableUnchanged()
    {
        var fixture=Fixture("nsw-journey-test.json");
        var valid=fixture.Array("journeys")![0]!.DeepClone();
        var early=valid.DeepClone();
        early.Array("legs")![0]!["origin"]!["departureTimePlanned"]=Departure.AddMinutes(-2).ToString("O");
        fixture["journeys"]=new JsonArray(early,valid);
        var steps=NswJourneyProvider.Parse(fixture,Departure);
        Assert.True(steps[0].DepartureTime>=Departure);
        var expected=NswJourneyProvider.Parse(new JsonObject{["journeys"]=new JsonArray(valid.DeepClone())},Departure);
        Assert.Equal(expected.Select(x=>x.DepartureTime),steps.Select(x=>x.DepartureTime));
        Assert.Equal(expected.Select(x=>x.ArrivalTime),steps.Select(x=>x.ArrivalTime));
    }
    [Fact] public void NswRejectsAllAlternativesThatRequireLeavingBeforeRequestedTime()
    {
        var fixture=Fixture("nsw-journey-test.json");
        fixture.Array("journeys")![0]!.Array("legs")![0]!["origin"]!["departureTimePlanned"]=Departure.AddMinutes(-2).ToString("O");
        Assert.Throws<ServiceException>(()=>NswJourneyProvider.Parse(fixture,Departure));
    }
    [Fact] public async Task NswNetworkFailureDoesNotExposeProviderBodyOrCredentials()
    {
        var handler=new CaptureHandler(_=>new JsonObject(),HttpStatusCode.Forbidden);var config=Config();var client=new HttpClient(handler);
        var provider=new NswJourneyProvider(client,Secrets(),new NavigationService(client,config));
        var error=await Assert.ThrowsAsync<ServiceException>(()=>provider.RouteAsync(from,to,"transit",Departure,default));
        Assert.DoesNotContain("synthetic-nsw-key",error.Message);Assert.DoesNotContain("Authorization",error.Message);
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    private sealed class CaptureHandler(Func<HttpRequestMessage,JsonObject> respond,HttpStatusCode status=HttpStatusCode.OK):HttpMessageHandler
    {
        public int Count{get;private set;}public JsonObject? Body{get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();Count++;if(request.Content is not null)Body=JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();return new(status){Content=new StringContent(respond(request).ToJsonString(),Encoding.UTF8,"application/json")};}
    }
}
