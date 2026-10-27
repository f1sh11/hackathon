using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace SydneyBuddy.Api.Tests;
public sealed class TravelDataTests {
    static TravelSnapshot Snapshot()=>new(new JsonObject(),new JsonArray(new JsonObject{["id"]="public"}),new(),"now");
    sealed class Warehouse(bool fail=false):ITravelWarehouse {
        public int Calls;
        public Task<TravelSnapshot> ReadAsync(CancellationToken token){Calls++;if(fail)throw new ServiceException("warehouse unavailable");return Task.FromResult(Snapshot());}
    }
    [Fact] public async Task ManagedCloudIgnoresClientSourceAndLoadsWarehouse() {
        var context=new DefaultHttpContext();context.Request.Path="/catalog";context.Request.Headers[TravelDataContext.Header]="direct";
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TravelData:ManagedSource","zetaris-cloud"}}).Build();
        var selection=new TravelDataContext(new HttpContextAccessor{HttpContext=context},config);var warehouse=new Warehouse();
        await new TravelDataMiddleware(_=>Task.CompletedTask).InvokeAsync(context,selection,warehouse);
        Assert.Equal("zetaris-cloud",selection.Mode);Assert.True(selection.Explicit);Assert.Equal(1,warehouse.Calls);Assert.Equal("zetaris-cloud",context.Response.Headers[TravelDataContext.Header]);
    }
    [Fact] public void ManagedCloudAlsoWorksWithoutAnyAppSourceSetting() {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TravelData:ManagedSource","zetaris-cloud"}}).Build();
        var selection=new TravelDataContext(new HttpContextAccessor{HttpContext=new DefaultHttpContext()},config);
        Assert.Equal("zetaris-cloud",selection.ManagedMode);Assert.True(selection.Warehouse);Assert.True(selection.Explicit);
    }
    [Fact] public void InvalidManagedSourceFailsRatherThanFallingBack() {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TravelData:ManagedSource","invalid"}}).Build();
        Assert.Throws<ServiceException>(()=>new TravelDataContext(new HttpContextAccessor(),config).Mode);
    }
    [Fact] public async Task DirectRequestsNeverContactWarehouseAndSelectionIsPerRequest(){
        var accessor=new HttpContextAccessor();var selection=new TravelDataContext(accessor);var warehouse=new Warehouse();var next=0;
        var middleware=new TravelDataMiddleware(_=>{next++;return Task.CompletedTask;});
        var direct=new DefaultHttpContext();direct.Request.Path="/catalog";direct.Request.Headers[TravelDataContext.Header]="direct";accessor.HttpContext=direct;
        await middleware.InvokeAsync(direct,selection,warehouse);Assert.Equal(0,warehouse.Calls);Assert.Null(selection.Snapshot);
        var zetaris=new DefaultHttpContext();zetaris.Request.Path="/trips";zetaris.Request.Method="POST";zetaris.Request.Headers[TravelDataContext.Header]="zetaris";accessor.HttpContext=zetaris;
        await middleware.InvokeAsync(zetaris,selection,warehouse);Assert.Equal(1,warehouse.Calls);Assert.NotNull(selection.Snapshot);
        accessor.HttpContext=direct;Assert.False(selection.Warehouse);Assert.Null(selection.Snapshot);Assert.Equal(2,next);
    }
    [Fact] public async Task LiveWeatherMiddlewareDoesNotLoadStaticWarehouseSnapshots(){
        var context=new DefaultHttpContext();context.Request.Path="/weather";context.Request.Headers[TravelDataContext.Header]="zetaris";
        var warehouse=new Warehouse(true);var invoked=false;var middleware=new TravelDataMiddleware(_=>{invoked=true;return Task.CompletedTask;});
        await middleware.InvokeAsync(context,new(new HttpContextAccessor{HttpContext=context}),warehouse);Assert.True(invoked);Assert.Equal(0,warehouse.Calls);
        var forecast=PlanValidator.ValidateForecast(new JsonObject{["date"]="2026-10-24",["days"]=2,["mustVisit"]=new JsonArray("not-needed-for-weather")});Assert.Equal(2,forecast.Int("days"));
        Assert.Equal(13,PlanValidator.ValidateForecast(new JsonObject{["date"]= "2026-10-24",["days"]=13}).Int("days"));
        Assert.Throws<ServiceException>(()=>PlanValidator.ValidateForecast(new JsonObject{["date"]="2026-10-24",["days"]=16}));
        Assert.Throws<ServiceException>(()=>PlanValidator.ValidateForecast(new JsonObject{["date"]="invalid"}));
    }
    [Fact] public async Task WarehouseFailureDoesNotSilentlyUseDirect(){
        var context=new DefaultHttpContext();context.Request.Path="/poi/search";context.Request.Headers[TravelDataContext.Header]="zetaris";
        var middleware=new TravelDataMiddleware(_=>throw new Exception("Must not continue"));
        await Assert.ThrowsAsync<ServiceException>(()=>middleware.InvokeAsync(context,new(new HttpContextAccessor{HttpContext=context}),new Warehouse(true)));
    }
    [Fact] public void InvalidSourceIsRejected(){var c=new DefaultHttpContext();c.Request.Headers[TravelDataContext.Header]="arbitrary";Assert.Throws<ServiceException>(()=>new TravelDataContext(new HttpContextAccessor{HttpContext=c}).Mode);}
    [Fact] public void StaleAndMissingForecastsAreUnknown(){
        var now=DateTimeOffset.UtcNow;var date=now.ToString("yyyy-MM-dd");var rows=new JsonArray(new JsonObject{["date"]=date,["available"]=true,["rainy"]=true,["fetchedAt"]=now.AddHours(-7).ToString("O")});
        var result=PublicTravelWeather.Forecast(rows,new JsonObject{["date"]=date,["days"]=2},"zetaris",now);
        Assert.Equal("unavailable",result.Str("status"));Assert.False(result.Bool("rainy"));Assert.All(result.Array("days")!,r=>Assert.False(r!.Bool("available")));
    }
    [Fact] public void FreshForecastPreservesRainAndSource(){
        var now=DateTimeOffset.UtcNow;var date=now.ToString("yyyy-MM-dd");var rows=new JsonArray(new JsonObject{["date"]=date,["available"]=true,["rainy"]=true,["temperatureMin"]=10,["temperatureMax"]=20,["precipitationProbability"]=80,["fetchedAt"]=now.ToString("O")});
        var result=PublicTravelWeather.Forecast(rows,new JsonObject{["date"]=date,["days"]=1},"zetaris",now);
        Assert.Equal("ok",result.Str("status"));Assert.True(result.Bool("rainy"));Assert.Equal("zetaris",result.Str("dataSource"));
    }
    static JsonObject WeatherRaw()=>JsonNode.Parse("""{"timezone":"Australia/Sydney","daily":{"time":["2026-10-24"],"temperature_2m_min":[15],"temperature_2m_max":[25],"precipitation_probability_max":[70],"weather_code":[61]}}""")!.AsObject();
    sealed class LiveWeatherWarehouse:ITravelWarehouse {
        public int Queries;public bool Fail;
        public Task<TravelSnapshot> ReadAsync(CancellationToken token)=>throw new Exception("Weather must not read a stored snapshot");
        public Task<JsonObject> WeatherRawAsync(CancellationToken token){Queries++;if(Fail)throw new ServiceException("weather offline");return Task.FromResult(WeatherRaw());}
    }
    sealed class WeatherHandler:HttpMessageHandler {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(WeatherRaw().ToJsonString())});}
    }
    [Fact] public async Task EveryZetarisWeatherRequestQueriesLiveApiWithoutSnapshotOrDirectFallback(){
        var context=new DefaultHttpContext();context.Request.Headers[TravelDataContext.Header]="zetaris";
        var selection=new TravelDataContext(new HttpContextAccessor{HttpContext=context});var handler=new WeatherHandler();var warehouse=new LiveWeatherWarehouse();
        var service=new PublicTravelWeather(new HttpClient(handler),selection,warehouse);
        var first=await service.GetAsync(TestSupport.Profile(),default);await service.GetAsync(TestSupport.Profile(),default);
        Assert.Equal(2,warehouse.Queries);Assert.Equal(0,handler.Calls);Assert.False(first.Bool("cached"));Assert.True(first.Bool("live"));
        Assert.Equal(61,first.Array("days")![0]!.Int("weatherCode"));
        warehouse.Fail=true;await Assert.ThrowsAsync<ServiceException>(()=>service.GetAsync(TestSupport.Profile(),default));Assert.Equal(0,handler.Calls);
    }
    [Fact] public async Task DirectWeatherIsNotCachedEither(){
        var selection=new TravelDataContext(new HttpContextAccessor{HttpContext=new DefaultHttpContext()});var handler=new WeatherHandler();
        var service=new PublicTravelWeather(new HttpClient(handler),selection,new LiveWeatherWarehouse());
        await service.GetAsync(TestSupport.Profile(),default);await service.GetAsync(TestSupport.Profile(),default);Assert.Equal(2,handler.Calls);
    }
    [Fact] public void CommunityImportsPreserveGuidePhotosAndDoNotInventReviews(){
        var value=new JsonObject{["id"]="gem:"+new string('a',24),["name"]="Public reserve",["source"]="Wikivoyage",["lat"]=-33.85,["lng"]=151.21,["guide"]=new JsonObject{["directions"]="Public path"},["photos"]=new JsonArray(new JsonObject{["artist"]="Community author",["license"]="CC BY-SA 4.0"}),["reviews"]=new JsonArray()};
        var imported=PoiService.Import(value);Assert.True(PoiService.IsImported(imported));Assert.Equal("Public path",imported.Object("guide")!.Str("directions"));Assert.Single(imported.Array("photos")!);Assert.Empty(imported.Array("reviews")!);
    }
    sealed class Handler:HttpMessageHandler {
        public int Calls;public string Sql="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken token){Calls++;if(Calls==1)return new(HttpStatusCode.OK){Content=new StringContent("{\"idToken\":\"private-test-token\",\"orgId\":\"test-org\"}")};
            Sql=await r.Content!.ReadAsStringAsync(token);
            Assert.Equal("Bearer",r.Headers.Authorization?.Scheme);
            // Simulate a service truncating the snapshot: never accept partial results.
            return new(HttpStatusCode.OK){Content=new StringContent("{\"data\":[[\"poi\",\"{}\"]],\"total\":6000}")};
        }
    }
    [Fact] public async Task TruncatedWarehouseSnapshotIsRejectedAndQueryIsFixed(){
        var handler=new Handler();using var cache=new MemoryCache(new MemoryCacheOptions());
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Zetaris:Username","test"},{"Zetaris:Password","test"}}).Build();
        await Assert.ThrowsAsync<ServiceException>(()=>new ZetarisTravelWarehouse(new HttpClient(handler),config,cache).ReadAsync(default));
        Assert.Contains("sydneybuddy_travel.travel_poi",handler.Sql);Assert.DoesNotContain("private-test-token",handler.Sql);Assert.Equal(2,handler.Calls);
    }
}
