using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
namespace SydneyBuddy.Api.Tests;
public sealed class TravelInsightTests {
    static JsonObject Place()=>new(){["id"]="test",["name"]="Museum",["lat"]=-33.86,["lng"]=151.21};
    [Fact] public void MissingPricesAndCrowdsAreNotZero(){
        var value=TravelInsightService.Price(Place(),new());Assert.Equal("unknown",value.Str("status"));Assert.Null(value["amount"]);Assert.Contains("尚未計入",value.Str("summary"));
    }
    [Fact] public void OsmPriceMustMatchBothNameAndNearbyCoordinate(){
        var feed=JsonNode.Parse("""{"elements":[{"type":"node","id":12,"lat":-33.86,"lon":151.21,"tags":{"name":"Museum","charge":"20 AUD"}}]}""")!.AsObject();
        var price=TravelInsightService.Price(Place(),feed);Assert.Equal("reported",price.Str("status"));Assert.Contains("20 AUD",price.Str("summary"));Assert.Null(price["amount"]);
        feed.Array("elements")![0]!["lat"]=-34;Assert.Equal("unknown",TravelInsightService.Price(Place(),feed).Str("status"));
    }
    [Fact] public void ZeroPlaceholderDoesNotBecomeConfirmedFree(){var place=Place();place["cost"]=0;Assert.Equal("unknown",TravelInsightService.Price(place,new()).Str("status"));}
    [Fact] public void IncidentsAreFilteredByDistanceAndEndStatusAndDoNotClaimEta(){
        var feed=JsonNode.Parse("""{"features":[{"id":1,"geometry":{"type":"Point","coordinates":[151.21,-33.86]},"properties":{"ended":false,"mainCategory":"ROAD CLOSURE","adviceA":"Avoid road"}},{"id":2,"geometry":{"type":"Point","coordinates":[151.21,-34.9]},"properties":{"ended":false}},{"id":3,"properties":{}},{"id":4,"geometry":{"type":"Point","coordinates":[151.21,-33.86]},"properties":{"ended":true}}]}""")!.AsObject();
        var result=TravelInsightService.Traffic(feed,new JsonArray(Place()),DateTimeOffset.UtcNow);Assert.Single(result.Array("items")!);Assert.False(result.Bool("etaAvailable"));Assert.False(result.Bool("speedAvailable"));Assert.Equal("now",result.Str("appliesTo"));
    }
    [Fact] public void FailedFeedIsNotReportedAsClearRoads(){Assert.Equal("unavailable",TravelInsightService.Traffic(new(),new JsonArray(Place()),DateTimeOffset.UtcNow).Str("status"));}
    [Fact] public void InputRejectsInvalidCoordinatesAndTooManyPlaces(){
        var bad=Place();bad["lat"]=999;Assert.Throws<ServiceException>(()=>TravelInsightService.Points(new JsonObject{["places"]=new JsonArray(bad)}));
        var many=new JsonArray();for(var i=0;i<31;i++)many.Add(Place());Assert.Throws<ServiceException>(()=>TravelInsightService.Points(new JsonObject{["places"]=many}));
    }
    [Fact] public void GooglePriceKeepsRangeWithoutInventingAverage(){
        var value=TravelInsightService.GooglePrice(JsonNode.Parse("""{"priceRange":{"startPrice":{"currencyCode":"AUD","units":"20"},"endPrice":{"currencyCode":"AUD","units":"40"}}}""")!.AsObject());Assert.Equal("reported",value.Str("status"));Assert.Null(value["amount"]);Assert.Equal("20",value.Object("range")!.Object("startPrice")!.Str("units"));
    }
    sealed class Handler:HttpMessageHandler {public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t){Calls++;throw new Exception("Warehouse mode must not contact sources directly");}}
    sealed class Warehouse(bool fail):ITravelWarehouse {
        public int Calls;
        public Task<TravelSnapshot> ReadAsync(CancellationToken t)=>throw new Exception("No static snapshot for insights");
        public Task<JsonObject> InsightRawAsync(string kind,CancellationToken t){Calls++;if(fail)throw new ServiceException("offline");return Task.FromResult(kind=="traffic"?new JsonObject{["features"]=new JsonArray()}:new JsonObject{["elements"]=new JsonArray()});}
    }
    [Theory][InlineData(false)][InlineData(true)]public async Task ZetarisRequestsDoNotBypassWarehouseOrFabricateCrowds(bool fail){
        var c=new DefaultHttpContext();c.Request.Headers[TravelDataContext.Header]="zetaris";var selection=new TravelDataContext(new HttpContextAccessor{HttpContext=c});var handler=new Handler();var warehouse=new Warehouse(fail);var config=new ConfigurationBuilder().Build();
        var paths=new ProjectPaths(Path.GetTempPath());var service=new TravelInsightService(new HttpClient(handler),selection,warehouse,paths,new SecretStore(paths),new PaidServicePolicy(config));
        var value=await service.GetAsync(new JsonObject{["places"]=new JsonArray(Place()),["mapProvider"]="apple"},default);
        Assert.Equal(2,warehouse.Calls);Assert.Equal(0,handler.Calls);Assert.Equal("zetaris",value.Str("dataSource"));Assert.Equal("unknown",value.Array("places")![0]!.Object("crowd")!.Str("status"));Assert.Null(value.Array("places")![0]!.Object("crowd")!["waitMinutes"]);
        Assert.Equal(fail?"unavailable":"reported",value.Object("traffic")!.Str("status"));
    }
    sealed class GoogleHandler:HttpMessageHandler {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t){Calls++;Assert.Equal("places.googleapis.com",r.RequestUri!.Host);Assert.True(r.Headers.Contains("X-Goog-Api-Key"));return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"priceLevel\":\"PRICE_LEVEL_MODERATE\"}")});}
    }
    [Theory][InlineData("google",true,5)][InlineData("apple",true,0)][InlineData("google",false,0)]
    public async Task PaidGoogleSupplementHonorsMapPolicyOptInAndFivePlaceBudget(string map,bool allowed,int expected){
        var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            File.WriteAllText(Path.Combine(root,".secrets.json"),"{\"GOOGLE_SERVER_KEY\":\"test-key-not-real\"}");
            var c=new DefaultHttpContext();c.Request.Headers[TravelDataContext.Header]="zetaris";
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"AllowPaidServices",allowed.ToString()}}).Build();var paths=new ProjectPaths(root);var handler=new GoogleHandler();
            var service=new TravelInsightService(new HttpClient(handler),new TravelDataContext(new HttpContextAccessor{HttpContext=c}),new Warehouse(false),paths,new SecretStore(paths),new PaidServicePolicy(config));
            var points=new JsonArray();for(var i=0;i<6;i++){var point=Place();point["id"]="google:fake"+i;points.Add(point);}
            var result=await service.GetAsync(new JsonObject{["places"]=points,["mapProvider"]=map},default);
            Assert.Equal(expected,handler.Calls);
            if(expected>0){Assert.Equal("google-direct-supplement",result.Array("places")![0]!.Object("price")!.Str("retrievalPath"));Assert.Equal("request-budget-reached",result.Array("places")![5]!.Str("googleFallback"));}
        }finally{Directory.Delete(root,true);}
    }
    [Fact]public void NearbyPriceMarkersExcludeUnrelatedPrivateRentalRooms(){
        var raw=JsonNode.Parse("""{"elements":[{"type":"node","id":1,"lat":-33.86,"lon":151.21,"tags":{"name":"Party Room","charge":"200 AUD/hour"}},{"type":"node","id":2,"lat":-33.86,"lon":151.21,"tags":{"name":"Pool","charge":"8 AUD","leisure":"swimming_pool"}}]}""")!.AsObject();
        Assert.Single(TravelInsightService.FeeMarkers(raw,new JsonArray(Place())));
    }

}
