using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;

namespace SydneyBuddy.Api.Tests;

public sealed class MeterlessPlanningTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"meterless-planning-"+Guid.NewGuid().ToString("N"));
    public MeterlessPlanningTests() {Directory.CreateDirectory(Path.Combine(root,"server"));File.Copy(Path.Combine(TestSupport.RepositoryRoot(),"server","places.json"),Path.Combine(root,"server","places.json"));}
    PlanningAgentSettings Settings(string model="meterless/auto",string token="fixture-token")=>new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
        ["PlanningAgent:Provider"]="meterless",["Meterless:BaseUrl"]="http://127.0.0.1:8088/v1/chat/completions",["Meterless:Token"]=token,["Meterless:Model"]=model}).Build());
    PlanningAgent Agent(Handler handler,PlanningAgentSettings? settings=null) {var config=settings??Settings();return new(new HttpClient(handler),new SecretStore(new ProjectPaths(root)),config,new MeterlessRuntime(new HttpClient(handler),config),new FakeMemory());}
    static JsonObject[] Activities()=>[new(){["id"]="osm:node:1",["name"]="Garden",["indoor"]=false,["indoorKnown"]=true,["category"]="自然"},new(){["id"]="osm:node:2",["name"]="Museum",["indoor"]=true,["category"]="文化"}];
    static JsonObject[] Stays()=>[new(){["id"]="osm:node:3",["name"]="Test hotel",["category"]="住宿",["poiKind"]="hotel",["nightlyRateAud"]=200}];
    static JsonObject Output()=>new(){["preferredIds"]=new JsonArray("p1","p0"),["dayPlans"]=new JsonArray(new JsonObject{["date"]="2026-10-24",["preferredIds"]=new JsonArray("p1")},new JsonObject{["date"]="2026-10-25",["preferredIds"]=new JsonArray("p0")}),["accommodationIds"]=new JsonArray("s0"),["explanation"]="Indoor culture first, garden on the other day.",["outstandingChecks"]=new JsonArray("Hotel availability and weather alerts are unverified.")};
    [Theory]
    [InlineData("127.0.0.1:8088")][InlineData("http://127.0.0.1:8088/")][InlineData("http://127.0.0.1:8088/v1")]
    [InlineData("http://127.0.0.1:8088/v1/chat/completions")][InlineData("http://127.0.0.1:8088/v1/models")][InlineData("http://127.0.0.1:8088/healthz")]
    public void NormalizesKnownRuntimeUrlsWithoutDuplicatingV1(string url)=>Assert.Equal("http://127.0.0.1:8088/v1/",PlanningAgentSettings.Normalize(url).AbsoluteUri);
    [Theory]
    [InlineData("http://remote.example/v1")][InlineData("https://user:password@example.com/v1")][InlineData("https://example.com/v1?token=bad")][InlineData("https://example.com/connect#k=bad")][InlineData("https://example.com/v1/v1")]
    public void RejectsAmbiguousOrCredentialBearingUrls(string url)=>Assert.Throws<ServiceException>(()=>PlanningAgentSettings.Normalize(url));
    [Fact]public void RemoteTlsAndExplicitContainerHttpAreSupported(){Assert.Equal("https://runtime.example/v1/",PlanningAgentSettings.Normalize("https://runtime.example").AbsoluteUri);Assert.Equal("http://host.docker.internal:8088/v1/",PlanningAgentSettings.Normalize("http://host.docker.internal:8088",true).AbsoluteUri);}
    [Theory][InlineData(401)][InlineData(403)]
    public async Task WrongTokenIsUnauthorizedRatherThanOffline(int status){var h=new Handler{Status=(HttpStatusCode)status};var result=await new MeterlessRuntime(new HttpClient(h),Settings()).StatusAsync(default);Assert.Equal("unauthorized",result.Str("status"));Assert.Equal("/v1/models",Assert.Single(h.Paths));Assert.Equal("Bearer fixture-token",h.Authorization);}
    [Theory][InlineData(500)][InlineData(302)]
    public async Task FailedModelsEndpointIsOffline(int status){var h=new Handler{Status=(HttpStatusCode)status};Assert.Equal("offline",(await new MeterlessRuntime(new HttpClient(h),Settings()).StatusAsync(default)).Str("status"));Assert.Null(h.Payload);}
    [Fact]public async Task PlaceholderTokenDoesNotMakeANetworkCall(){var h=new Handler();var runtime=new MeterlessRuntime(new HttpClient(h),Settings(token:"rt-local"));Assert.Equal("not_configured",(await runtime.StatusAsync(default)).Str("status"));await Assert.ThrowsAsync<ServiceException>(()=>runtime.CompleteAsync("skill",new(),default));Assert.Empty(h.Paths);}
    [Fact]public async Task UnknownMeterlessModelIdsRemainAvailable(){var h=new Handler{Model="meterless/new-mode"};var runtime=new MeterlessRuntime(new HttpClient(h),Settings("meterless/new-mode"));var status=await runtime.StatusAsync(default);Assert.Contains(status.Array("models")!,x=>x!.ToString()=="meterless/new-mode");await runtime.CompleteAsync("skill",new(),default);Assert.Equal("meterless/new-mode",h.Payload!.Str("model"));}
    [Fact]public async Task ModelMismatchDoesNotSilentlyChooseAnotherModel(){var h=new Handler();await Assert.ThrowsAsync<ServiceException>(()=>new MeterlessRuntime(new HttpClient(h),Settings("meterless/missing")).CompleteAsync("skill",new(),default));Assert.Null(h.Payload);}
    [Fact]public async Task SingleAgentGetsWeatherInterestsSettingsAndSeparateStaysWithoutMutatingInputs(){
        var h=new Handler();var profile=TestSupport.Profile(2);profile["language"]="en";profile["mustVisit"]=new JsonArray("osm:node:2");profile["indoorPreference"]="preferIndoor";
        var weather=new JsonObject{["days"]=new JsonArray(new JsonObject{["date"]="2026-10-24",["rainy"]=true,["available"]=true},new JsonObject{["date"]="2026-10-25",["available"]=false})};
        var before=profile.ToJsonString();var result=await Agent(h).ProposeAsync(profile,Activities(),weather,Stays(),default);
        Assert.Equal(before,profile.ToJsonString());Assert.Equal("osm:node:2",result.Array("preferredIds")![0]!.ToString());Assert.Equal("osm:node:3",Assert.Single(result.Array("accommodationIds")!).ToString());
        Assert.Equal(new[]{"/v1/models","/v1/chat/completions"},h.Paths);Assert.Equal("sydneybuddy",h.App);Assert.Equal("user",h.Scope);
        var messages=h.Payload!.Array("messages")!;Assert.Contains("Weather × activity setting × interests",messages[0]!.Str("content"));Assert.IsAssignableFrom<JsonValue>(messages[1]!["content"]);
        var data=JsonNode.Parse(messages[1]!.Str("content"))!.AsObject();Assert.Equal(weather.ToJsonString(),data["weather"]!.ToJsonString());Assert.Equal("preferIndoor",data.Object("trip")!.Str("indoorPreference"));Assert.Equal("p1",Assert.Single(data.Object("trip")!.Array("mustVisit")!).ToString());Assert.False(data.Array("accommodations")![0]!.Bool("availabilityVerified"));
        Assert.Equal("outdoor",data.Array("candidates")![0]!.Str("setting"));Assert.DoesNotContain("osm:node:1",messages[1]!.Str("content"));
    }
    [Theory][InlineData("unknown")][InlineData("duplicate")][InlineData("stayAsAttraction")][InlineData("duplicateDay")][InlineData("repeatAcrossDays")][InlineData("wrongDate")][InlineData("missingMust")][InlineData("inventedPrice")][InlineData("invalidChecks")]
    public async Task RejectsFabricatedRepeatedOrOutOfScopeAgentOutput(string defect){
        var h=new Handler();var output=Output();var profile=TestSupport.Profile(2);profile["mustVisit"]=new JsonArray("osm:node:1");
        switch(defect){
            case "unknown":output["preferredIds"]=new JsonArray("made-up");break;
            case "duplicate":output["preferredIds"]=new JsonArray("p0","p0");break;
            case "stayAsAttraction":output["preferredIds"]=new JsonArray("s0");break;
            case "duplicateDay":output.Array("dayPlans")![1]!["date"]="2026-10-24";break;
            case "repeatAcrossDays":output.Array("dayPlans")![1]!["preferredIds"]=new JsonArray("p1");break;
            case "wrongDate":output.Array("dayPlans")![0]!["date"]="2026-11-01";break;
            case "missingMust":output["preferredIds"]=new JsonArray("p1");break;
            case "inventedPrice":output["confirmedPrice"]=1;break;
            case "invalidChecks":output["outstandingChecks"]=new JsonArray(new JsonObject());break;
        }
        h.Output=output;await Assert.ThrowsAsync<ServiceException>(()=>Agent(h).ProposeAsync(profile,Activities(),new(),Stays(),default));
    }
    [Fact]public async Task MissingRuntimeNeverFallsBackToNvidia(){var h=new Handler{Status=HttpStatusCode.ServiceUnavailable};await Assert.ThrowsAsync<ServiceException>(()=>Agent(h).ProposeAsync(TestSupport.Profile(2),Activities(),new(),Stays(),default));Assert.All(h.Paths,p=>Assert.Equal("/v1/models",p));}
    [Fact]public async Task CancellationIsNotReportedAsAnOfflineRuntime(){using var cancellation=new CancellationTokenSource();cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new MeterlessRuntime(new HttpClient(new Handler()),Settings()).StatusAsync(cancellation.Token));}
    [Fact]public void BothSkillsAreEmbeddedAndContainTheSameOutputFields(){foreach(var lang in new[]{"en","zh-Hant"}){var text=ItinerarySkill.Read(lang);foreach(var field in new[]{"preferredIds","dayPlans","accommodationIds","outstandingChecks"})Assert.Contains(field,text);}}
    [Fact]public void ExplicitWeatherPreferencesAreValidated(){var p=TestSupport.Profile();p["weatherTolerance"]="invented";Assert.Throws<ServiceException>(()=>PlanValidator.Validate(p,Activities()));p["weatherTolerance"]="avoidRain";p["indoorPreference"]="preferIndoor";Assert.Equal("avoidRain",PlanValidator.Validate(p,Activities()).Str("weatherTolerance"));}

    PlanningInventory Inventory(string mode) {
        var paths=new ProjectPaths(root);var config=new ConfigurationBuilder().Build();var http=new DefaultHttpContext();
        if(mode!="direct")http.Request.Headers[TravelDataContext.Header]=mode;
        var travel=new TravelDataContext(new HttpContextAccessor{HttpContext=http});
        var points=Activities().Concat(Stays()).Select(p=>{var copy=p.DeepClone().AsObject();copy["lat"]=-33.861;copy["lng"]=151.212;copy["source"]="OpenStreetMap";return (JsonNode)copy;}).ToArray();
        if(mode=="direct"){
            var folder=Path.Combine(root,"infra","zetaris","data");Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"osm-map-places.json"),new JsonObject{["places"]=new JsonArray(points)}.ToJsonString());
        }else http.Items[typeof(TravelSnapshot)]=new TravelSnapshot(new JsonObject{["places"]=new JsonArray()},new JsonArray(),new JsonArray(),"2026-10-27T00:00:00Z",Osm:new JsonArray(points));
        var cat=new PlaceCatalog(new CatalogCache(),paths,travel);
        return new(new UnifiedPoiService(travel,new OverturePoiProvider(paths,config),new HiddenGemService(travel,paths),paths,cat),travel);
    }
    [Theory][InlineData("direct")][InlineData("zetaris")][InlineData("zetaris-cloud")]
    public void InventorySeparatesStaysAndPreservesSelectedSource(string mode){
        var inventory=Inventory(mode).Read(TestSupport.Profile(),default);Assert.Equal(mode,inventory.Str("dataSource"));
        Assert.Contains(inventory.Array("activities")!,p=>p!.Str("id")=="osm:node:1");
        Assert.DoesNotContain(inventory.Array("activities")!,p=>PlanningInventory.IsAccommodation(p!.AsObject()));
        Assert.Equal("osm:node:3",Assert.Single(inventory.Array("accommodations")!).Str("id"));Assert.False(inventory.Bool("availabilityVerified"));
    }
    [Fact]public async Task PlannerPipelineUsesInventoryAndLeavesHotelOutsideRouteAndBudget(){
        var inventory=Inventory("direct");var h=new Handler{Respond=payload=>{
            var input=JsonNode.Parse(payload.Array("messages")![1]!.Str("content"))!.AsObject();
            return new(){["preferredIds"]=new JsonArray(input.Array("candidates")!.OfType<JsonObject>().Take(4).Select(x=>(JsonNode)JsonValue.Create(x.Str("id"))!).ToArray()),
                ["accommodationIds"]=new JsonArray("s0"),["explanation"]="Nature and culture with an unbooked stay.",["outstandingChecks"]=new JsonArray("Opening hours and hotel availability need checking.")};
        }};
        var paths=new ProjectPaths(root);var external=new FakeExternal();var cat=new PlaceCatalog(new CatalogCache(),paths);
        var planner=new Planner(new PlaceService(external,cat),new RecommendationService(external,new FakeMemory()),new RouteScheduler(new RouteService(external)),new WeatherService(external),new SecretStore(paths),agent:Agent(h),inventory:inventory);
        var profile=TestSupport.Profile();profile["budget"]=500;profile["maxSteps"]=60000;
        var draft=await planner.PlanAsync(profile,"demo");Assert.Equal("meterless",draft.Object("planningAgent")!.Str("provider"));Assert.Equal("draft_pending_verification",draft.Object("planningAgent")!.Str("status"));
        Assert.Equal("osm:node:3",Assert.Single(draft.Array("accommodationSuggestions")!).Str("id"));
        Assert.DoesNotContain(draft.Array("days")!.OfType<JsonObject>().SelectMany(d=>d.Array("stops")!.OfType<JsonObject>()),s=>PlanningInventory.IsAccommodation(s));
        Assert.Equal("环形码头",draft.Object("profile")!.Object("start")!.Str("name"));Assert.Null(draft["id"]);Assert.Empty(external.Calls);
        var shortlist=await planner.PrepareWalkingAsync(profile);Assert.NotNull(shortlist["planningAgent"]);Assert.NotNull(shortlist["accommodationSuggestions"]);Assert.Single(shortlist.Array("candidateDays")!);
    }
    [Fact]public async Task RainIntolerantTravelerGetsOutdoorRequiredVisitOnDryDayWithoutDuplicates(){
        var places=Activities();foreach(var p in places){p["lat"]=-33.861;p["lng"]=151.212;p["visitMinutes"]=60;p["cost"]=0;p["openMinute"]=600;p["closeMinute"]=1080;}
        var raw=TestSupport.Profile(2);raw["mustVisit"]=new JsonArray("osm:node:1");raw["weatherTolerance"]="avoidRain";
        var profile=PlanValidator.Validate(raw,places);var context=new PlanningContext("demo",new Dictionary<string,string>()){MeterlessPlanning=true};
        var weather=new JsonObject{["days"]=new JsonArray(new JsonObject{["date"]="2026-10-24",["rainy"]=true},new JsonObject{["date"]="2026-10-25",["rainy"]=false})};
        var days=await new RouteScheduler(new RouteService(new FakeExternal())).ScheduleDaysAsync(profile,places,weather,new Dictionary<string,int>(),context);
        Assert.DoesNotContain(days[0]!.Array("stops")!.OfType<JsonObject>(),p=>p.Str("id")=="osm:node:1");Assert.Contains(days[1]!.Array("stops")!.OfType<JsonObject>(),p=>p.Str("id")=="osm:node:1");
        var ids=days.OfType<JsonObject>().SelectMany(d=>d.Array("stops")!.OfType<JsonObject>()).Select(p=>p.Str("id")).ToArray();Assert.Equal(ids.Length,ids.Distinct().Count());
    }
    [Fact]public async Task AcceptingRainDoesNotRemoveOrShortenAnOutdoorVisitEvenInRainScenario(){
        var p=Activities()[0];p["lat"]=-33.861;p["lng"]=151.212;p["visitMinutes"]=90;p["cost"]=0;p["openMinute"]=600;p["closeMinute"]=1080;
        var raw=TestSupport.Profile();raw["weatherOverride"]="rain";raw["weatherTolerance"]="acceptRain";raw["indoorPreference"]="preferOutdoor";
        var profile=PlanValidator.Validate(raw,[p]);var context=new PlanningContext("demo",new Dictionary<string,string>()){MeterlessPlanning=true};
        var days=await new RouteScheduler(new RouteService(new FakeExternal())).ScheduleDaysAsync(profile,[p],new JsonObject{["rainy"]=true},new Dictionary<string,int>(),context);
        Assert.Equal(90,Assert.Single(days[0]!.Array("stops")!).Int("visitMinutes"));
    }

    sealed class Handler:HttpMessageHandler {
        public List<string> Paths=[];public string? Authorization,App,Scope;public JsonObject? Payload;
        public HttpStatusCode Status=HttpStatusCode.OK;public string Model="meterless/auto";public JsonObject Output=MeterlessPlanningTests.Output();public Func<JsonObject,JsonObject>? Respond;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            ct.ThrowIfCancellationRequested();Paths.Add(request.RequestUri!.AbsolutePath);Authorization=request.Headers.Authorization?.ToString();App=request.Headers.GetValues("meterless-app").Single();
            JsonObject body;
            if(request.Content is null)body=new(){["data"]=new JsonArray(new JsonObject{["id"]=Model})};
            else {Scope=request.Headers.GetValues("meterless-scope").Single();Payload=JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();body=new(){["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["content"]=(Respond?.Invoke(Payload)??Output).ToJsonString()}})};}
            return new(Status){Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")};
        }
    }
    public void Dispose()=>Directory.Delete(root,true);
}
