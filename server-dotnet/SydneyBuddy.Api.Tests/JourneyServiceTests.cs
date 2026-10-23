using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace SydneyBuddy.Api.Tests;
public sealed class JourneyServiceTests
{
    private static IConfiguration Config(string? otp = "http://127.0.0.1:8801/otp/gtfs/v1", string? roads = "http://127.0.0.1:8002/route") => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Transit:OtpGraphqlUrl"]=otp,["Navigation:ValhallaUrl"]=roads}).Build();
    private static JsonObject Input(params string[] modes) => JsonNode.Parse("""{"departureTime":"2026-10-23T10:00:00+11:00","points":[{"name":"测试起点","lat":-33.8611,"lng":151.2126},{"name":"测试终点","lat":-33.8568,"lng":151.2153}]}""")!.AsObject().Also(x=>x["modes"]=new JsonArray(modes.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()));
    private static JsonObject Fixture()=>JsonNode.Parse(File.ReadAllText(Path.Combine(TestSupport.RepositoryRoot(),"tests","fixtures","otp-journey-test.json")))!.AsObject();
    private static JourneyService Service(Handler handler,IConfiguration? config=null){config??=Config();return new JourneyService(new HttpClient(handler),config,new NavigationService(new HttpClient(handler),config));}
    [Fact] public async Task TransitKeepsBusNumberDirectionBoardingWaitAndPlannedState()
    {
        var handler=new Handler(_=>Fixture());var result=await Service(handler).PlanAsync(Input("transit"),default);
        var steps=result.Legs[0].Steps;Assert.Equal(new[]{"walk","bus","walk"},steps.Select(x=>x.Mode));
        Assert.Equal("TEST-01",steps[1].Line);Assert.Equal("测试目的地方向",steps[1].Headsign);
        Assert.Equal("测试上车站 A",steps[1].From);Assert.Equal("测试下车站 B",steps[1].To);
        Assert.Equal(300,steps[1].WaitSeconds);Assert.Equal(4,steps[1].StopCount);Assert.False(steps[1].Realtime);
        Assert.Equal(1800,result.TravelSeconds);Assert.Equal(-33.86108,steps[0].Geometry[0].Lat,5);
        var variables=handler.Requests[0].Object("variables")!;
        Assert.Equal(151.2126,variables.Object("origin")!.Object("location")!.Object("coordinate")!.Number("longitude"));
        Assert.Contains("planConnection",handler.Requests[0].Str("query"));Assert.Contains("transitOnly: true",handler.Requests[0].Str("query"));
    }
    [Fact] public async Task NextLegUsesArrivalPlusAttractionStayAndKeepsOrdering()
    {
        var request=Input("transit","transit");request.Array("points")![1]!["stayMinutes"]=60;
        request.Array("points")!.Add(new JsonObject{["name"]="返回",["lat"]=-33.8611,["lng"]=151.2126});
        var handler=new Handler(call=>{var result=Fixture();var legs=result.Object("data")!.Object("planConnection")!.Array("edges")![0]!.Object("node")!.Array("legs")!;
            var desired=DateTimeOffset.Parse(call.Object("variables")!.Object("dateTime")!.Str("earliestDeparture"));
            var shift=desired.ToUnixTimeMilliseconds()-legs[0]!["startTime"]!.GetValue<long>();
            foreach(var l in legs){l!["startTime"]=l["startTime"]!.GetValue<long>()+shift;l["endTime"]=l["endTime"]!.GetValue<long>()+shift;}return result;});
        var plan=await Service(handler).PlanAsync(request,default);
        var second=DateTimeOffset.Parse(handler.Requests[1].Object("variables")!.Object("dateTime")!.Str("earliestDeparture"));
        Assert.Equal(plan.Legs[0].ArrivalTime.AddHours(1),second);Assert.Equal(3600,plan.Legs[0].StaySeconds);
        Assert.Equal(3600,plan.TravelSeconds);Assert.Equal(7200,plan.ElapsedSeconds);
    }
    [Theory][InlineData(null)][InlineData("http://untrusted.example/graphql")][InlineData("broken")]
    public async Task MissingTransitFailsBeforeAnyStreetNetworkRequest(string? endpoint)
    {
        var request=Input("walk","transit");request.Array("points")!.Add(new JsonObject{["name"]="返回",["lat"]=-33.8611,["lng"]=151.2126});
        var handler=new Handler(_=>throw new Exception("Unexpected network"));var service=Service(handler,Config(endpoint));
        Assert.False(service.TransitConfigured);await Assert.ThrowsAsync<ServiceException>(()=>service.PlanAsync(request,default));Assert.Empty(handler.Requests);
    }
    [Fact] public async Task DriveUsesAutoAndExistingNavigationStaysPedestrian()
    {
        var handler=new Handler(_=>JsonNode.Parse("""{"code":"Ok","routes":[{"geometry":"_p~iF~ps|U_ulLnnqC_mqNvxq`@","distance":1000,"duration":120,"legs":[{}]}]}""")!.AsObject());
        var result=await Service(handler).PlanAsync(Input("drive"),default);Assert.Equal("drive",result.Legs[0].Steps[0].Mode);Assert.Equal("auto",handler.Requests[0].Str("costing"));
        var navigation=new NavigationService(new HttpClient(handler),Config());
        await navigation.RouteAsync(JsonNode.Parse("""{"locations":[{"lat":-33.86,"lon":151.21},{"lat":-33.85,"lon":151.22}],"costing":"auto"}""")!.AsObject(),default);
        Assert.Equal("pedestrian",handler.Requests[1].Str("costing"));
    }
    [Theory][InlineData("google")][InlineData("bad-mode")][InlineData("no-offset")]
    public async Task InvalidInputsNeverCallProviders(string variant)
    {
        var request=Input("walk");if(variant=="google")request["contentSource"]="google";if(variant=="bad-mode")request["modes"]=new JsonArray("flying");if(variant=="no-offset")request["departureTime"]="2026-10-23T10:00:00";
        var handler=new Handler(_=>throw new Exception());await Assert.ThrowsAsync<ServiceException>(()=>Service(handler).PlanAsync(request,default));Assert.Empty(handler.Requests);
    }
    [Fact] public async Task MissingLineIsExplicitAndDoesNotInventABusNumber()
    {
        var fixture=Fixture();fixture.Object("data")!.Object("planConnection")!.Array("edges")![0]!.Object("node")!.Array("legs")![1]!["route"]=new JsonObject();
        var plan=await Service(new Handler(_=>fixture)).PlanAsync(Input("transit"),default);Assert.Null(plan.Legs[0].Steps[1].Line);
    }
    [Theory][InlineData("errors")][InlineData("no-route")][InlineData("bad-time")][InlineData("booking")]
    public async Task IncompleteOrDemandResponsiveTripsDoNotBecomeNormalBusRoutes(string variant)
    {
        var fixture=Fixture();var node=fixture.Object("data")!.Object("planConnection")!.Array("edges")![0]!.Object("node")!;
        if(variant=="errors")fixture["errors"]=new JsonArray(new JsonObject{["message"]="private provider text"});
        if(variant=="no-route")node["legs"]=new JsonArray();
        if(variant=="bad-time")node.Array("legs")![1]!["startTime"]=0;
        if(variant=="booking")node.Array("legs")![1]!["pickupType"]="CALL_AGENCY";
        var exception=await Assert.ThrowsAsync<ServiceException>(()=>Service(new Handler(_=>fixture)).PlanAsync(Input("transit"),default));Assert.DoesNotContain("private provider text",exception.Message);
    }
    [Fact] public async Task CancellationPropagatesWithoutPublishingPartialPlan()
    {
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Service(new Handler(_=>Fixture())).PlanAsync(Input("transit"),cancellation.Token));
    }
    private sealed class Handler(Func<JsonObject,JsonObject> respond):HttpMessageHandler
    {
        public List<JsonObject> Requests{get;}=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){ct.ThrowIfCancellationRequested();var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();Requests.Add(body);return new(HttpStatusCode.OK){Content=new StringContent(respond(body).ToJsonString(),Encoding.UTF8,"application/json")};}
    }
}
internal static class JourneyTestExtensions { public static T Also<T>(this T value,Action<T> action){action(value);return value;} }
