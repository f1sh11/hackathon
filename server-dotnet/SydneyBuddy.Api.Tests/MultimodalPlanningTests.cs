using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
namespace SydneyBuddy.Api.Tests;

public sealed class MultimodalPlanningTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"multimodal-"+Guid.NewGuid());
    SecretStore Secrets(){Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,".secrets.json"),"{\"TFNSW_API_KEY\":\"test-not-a-real-secret\"}");return new(new ProjectPaths(root));}
    static JsonObject From()=>new(){["name"]="A",["lat"]=-33.8611,["lng"]=151.2126};
    static JsonObject To()=>new(){["name"]="B",["lat"]=-33.8568,["lng"]=151.2153};
    static JsonObject Walk(int minutes=60)=>new(){["meters"]=4000,["minutes"]=minutes,["source"]="Apple Maps / MapKit",["polyline"]=""};
    TripTransportRouter Router(HttpMessageHandler handler){var c=new HttpClient(handler);var config=new ConfigurationBuilder().Build();return new(new NswJourneyProvider(c,Secrets(),new NavigationService(c,config)),Secrets());}
    static JsonObject Profile(string policy="balanced")=>new(){["date"]="2026-10-23",["transportPreference"]=policy,["maxSteps"]=5000};
    [Fact]public async Task TransitIncludesAccessWalkingWaitingAndFareUnknownAndCacheIsDepartureSpecific(){
        var handler=new Handler();var router=Router(handler);var ctx=new PlanningContext("demo",new Dictionary<string,string>());
        var leg=await router.ChooseAsync(From(),To(),Walk(),Profile(),600,ctx,default);
        Assert.Equal("transit",leg.Str("mode"));Assert.True(leg.Int("walkingMeters")<leg.Int("meters"));Assert.False(leg.Bool("fareKnown"));Assert.Contains(leg.Array("steps")!.OfType<JsonObject>(),s=>s.Str("line")=="TEST-01");
        var steps=leg.Array("steps")!.OfType<JsonObject>().ToArray();Assert.True(leg.Int("minutes")>=steps.Sum(s=>s.Number("seconds")+s.Number("waitSeconds"))/60);
        await router.ChooseAsync(From(),To(),Walk(),Profile(),600,ctx,default);Assert.Equal(1,handler.Count);
        var wrapped=new JsonArray(new JsonObject{["from"]=From(),["to"]=To(),["date"]="2026-10-23",["minute"]=600,["leg"]=leg});
        var evidence=new TransportEvidence(wrapped);Assert.NotNull(evidence.Get(From(),To(),"2026-10-23",600));Assert.Null(evidence.Get(From(),To(),"2026-10-23",601));
    }
    [Fact]public async Task WalkingOnlyAndShortWalkNeverQueryTransit(){var h=new Handler();var router=Router(h);var ctx=new PlanningContext("demo",new Dictionary<string,string>());Assert.Equal("Apple Maps / MapKit",(await router.ChooseAsync(From(),To(),Walk(),Profile("walk"),600,ctx,default)).Str("source"));await router.ChooseAsync(From(),To(),Walk(8),Profile(),600,ctx,default);Assert.Equal(0,h.Count);}
    [Fact]public async Task VerifiedAbsenceOfTransitMayUseTheAlreadyVerifiedWalkingRoute() {
        var result=await Router(new Handler(noTransit:true)).ChooseAsync(From(),To(),Walk(),Profile(),600,new("demo",new Dictionary<string,string>()),default);
        Assert.Contains("Apple",result.Str("source"));Assert.NotEqual("transit",result.Str("mode"));
    }
    [Fact]public async Task BrokenTransitCannotBecomeASuccessfulWalkingFallback(){var h=new Handler(HttpStatusCode.Forbidden);var e=await Assert.ThrowsAsync<ServiceException>(()=>Router(h).ChooseAsync(From(),To(),Walk(),Profile(),600,new("demo",new Dictionary<string,string>()),default));Assert.Contains("未能核实",e.Message);Assert.DoesNotContain("test-not-a-real-secret",e.Message);}
    [Fact]public void DynamicSearchCandidatesDoNotBecomeVerifiedFreeOpenOrIndoor(){var p=new JsonObject{["id"]="apple:abc123",["name"]="Found Restaurant",["lat"]=-33.86,["lng"]=151.21,["category"]="美食",["source"]="Apple Maps",["poiKind"]="restaurant",["cost"]=0,["visitMinutes"]=45};var merged=PlanningCandidates.Merge([],new JsonArray(p));var item=Assert.Single(merged);Assert.True(RecommendationQuality.IsMeal(item));Assert.False(item.Bool("costKnown"));Assert.False(item.Bool("hoursVerified"));Assert.False(item.Bool("indoorKnown"));Assert.Equal(30,item.Number("cost"));Assert.Throws<ServiceException>(()=>PlanningCandidates.Merge([],new JsonArray(p.DeepClone(),p.DeepClone())));}
    [Fact]public void UnknownTransportPolicyIsRejected(){var p=TestSupport.Profile();p["transportPreference"]="teleport";Assert.Throws<ServiceException>(()=>PlanValidator.Validate(p,[]));}
    [Fact]public async Task RecalculationUsesTransitAccessStepsAndPreservesRequiredTiming() {
        var paths=new ProjectPaths(TestSupport.RepositoryRoot());var catalog=new PlaceCatalog(new CatalogCache(),paths);var editor=new ScheduleEditorService(catalog);
        var profile=PlanValidator.Validate(TestSupport.Profile(),catalog.Get());profile["start"]=From();profile["end"]=To();profile["transportPreference"]="lessWalking";profile["date"]="2026-10-23";profile["maxSteps"]=5000;
        var trip=new JsonObject{["mode"]="demo",["title"]="test",["profile"]=profile,["days"]=new JsonArray(),["weather"]=new JsonObject(),["warnings"]=new JsonArray(),["trace"]=new JsonArray(),["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        var evidence=new JsonArray(new JsonObject{["from"]=From(),["to"]=To(),["meters"]=4000,["seconds"]=3600,["fetchedAt"]=DateTimeOffset.UtcNow.ToString("O")});
        var days=new JsonArray(new JsonObject{["date"]="2026-10-23",["startMinute"]=600,["endMinute"]=1080,["activities"]=new JsonArray()});
        trip.Remove("days");
        var request=new JsonObject{["trip"]=trip,["days"]=days,["walkingRoutes"]=evidence};
        var updated=await new TransportScheduleService(editor,Router(new Handler())).BuildAsync(request,default);
        Assert.Equal("transit",updated.Array("days")![0]!.Array("legs")![0]!.Str("mode"));
        Assert.True(updated.Array("days")![0]!.Int("estimatedSteps")<5000);
        Assert.DoesNotContain(updated.Object("schedule")!.Array("issues")!.OfType<JsonObject>(),i=>i.Str("code")=="steps");
        var saved=editor.Build(new JsonObject{["trip"]=updated.DeepClone(),["days"]=updated.Object("schedule")!["days"]!.DeepClone(),["settings"]=updated.Object("schedule")!["settings"]!.DeepClone()});
        Assert.Equal("transit",saved.Array("days")![0]!.Array("legs")![0]!.Str("mode"));
    }
    [Fact]public async Task HistoricalTransitEvidenceKeepsItsProviderWhenNoWalkingEvidenceIsSupplied() {
        using var cache=new CatalogCache();var paths=new ProjectPaths(TestSupport.RepositoryRoot());var catalog=new PlaceCatalog(cache,paths);var editor=new ScheduleEditorService(catalog);
        var profile=TestSupport.Profile();profile["start"]=From();profile["end"]=To();profile["transportPreference"]="lessWalking";profile["date"]="2026-10-23";
        var leg=await Router(new Handler()).ChooseAsync(From(),To(),Walk(),Profile(),600,new("demo",new Dictionary<string,string>()),default);
        var route=new JsonObject{["from"]=From(),["to"]=To(),["date"]="2026-10-23",["minute"]=600,["leg"]=leg};
        var trip=new JsonObject{["mode"]="demo",["title"]="test",["profile"]=profile,["weather"]=new JsonObject(),["warnings"]=new JsonArray(),["trace"]=new JsonArray(),["createdAt"]=DateTimeOffset.UtcNow.ToString("O")};
        var updated=editor.Build(new JsonObject{["trip"]=trip,["transportRoutes"]=new JsonArray(route),["days"]=new JsonArray(new JsonObject{["date"]="2026-10-23",["startMinute"]=600,["endMinute"]=1080,["activities"]=new JsonArray()})},allowHistoricalTransit:true);
        var used=updated.Array("days")![0]!.Array("legs")![0]!.AsObject();
        Assert.Equal("transit",used.Str("mode"));Assert.Equal("Transport for NSW / Trip Planner",used.Str("source"));
        Assert.Contains("Transport for NSW / Trip Planner",updated.Object("schedule")!.Str("source"));
    }
    [Fact]public async Task EditingOneDayRetainsOtherDatesTransitEvidence() {
        var paths=new ProjectPaths(TestSupport.RepositoryRoot());var catalog=new PlaceCatalog(new CatalogCache(),paths);var editor=new ScheduleEditorService(catalog);
        var profile=PlanValidator.Validate(TestSupport.Profile(2),catalog.Get());profile["start"]=From();profile["end"]=To();profile["transportPreference"]="lessWalking";profile["date"]="2026-10-23";profile["maxSteps"]=5000;
        var leg=await Router(new Handler()).ChooseAsync(From(),To(),Walk(),Profile(),600,new("demo",new Dictionary<string,string>()),default);
        var trip=new JsonObject{["mode"]="demo",["title"]="test",["profile"]=profile,["weather"]=new JsonObject(),["warnings"]=new JsonArray(),["trace"]=new JsonArray(),["createdAt"]=DateTimeOffset.UtcNow.ToString("O"),["transportRoutes"]=new JsonArray(new JsonObject{["from"]=From(),["to"]=To(),["date"]="2026-10-24",["minute"]=600,["leg"]=leg})};
        var roads=new JsonArray(new JsonObject{["from"]=From(),["to"]=To(),["meters"]=4000,["seconds"]=3600,["fetchedAt"]=DateTimeOffset.UtcNow.ToString("O")});
        var days=new JsonArray();foreach(var date in new[]{"2026-10-23","2026-10-24"})days.Add(new JsonObject{["date"]=date,["startMinute"]=600,["endMinute"]=1080,["stay"]=From(),["activities"]=new JsonArray()});
        var saved=editor.Build(new JsonObject{["trip"]=trip,["days"]=days,["walkingRoutes"]=roads});
        var updated=await new TransportScheduleService(editor,Router(new Handler())).BuildAsync(new JsonObject{["trip"]=saved,["days"]=saved.Object("schedule")!["days"]!.DeepClone(),["walkingRoutes"]=roads.DeepClone(),["transportDays"]=new JsonArray(0)},default);
        Assert.Equal("transit",updated.Array("days")![1]!.Array("legs")![0]!.Str("mode"));
        Assert.Contains(updated.Array("transportRoutes")!.OfType<JsonObject>(),x=>x.Str("date")=="2026-10-24");
    }
    [Fact]public void MissingSearchAliasCannotMakeEveryImportedPlaceMatchAMustSee() {
        var profile=TestSupport.Profile();profile["mustVisit"]=new JsonArray();profile["mustDo"]="悉尼歌剧院";
        var opera=new JsonObject{["id"]="apple:opera",["name"]="悉尼歌剧院",["lat"]=-33.8568,["lng"]=151.2153};
        var restaurant=new JsonObject{["id"]="apple:restaurant",["name"]="Restaurant",["lat"]=-33.88,["lng"]=151.2};
        TravelConstraints.Apply(profile,[opera,restaurant]);
        var mustVisit=profile.Array("mustVisit")!;Assert.Equal("apple:opera",Assert.Single(mustVisit)!.GetValue<string>());
    }
    [Fact]public async Task AgentShortAliasesReturnRealIdsAndUnknownAliasesStillFailClosed() {
        var secrets=Secrets();File.WriteAllText(Path.Combine(root,".secrets.json"),"{\"NVIDIA_API_KEY\":\"test-not-a-real-secret\"}");
        var candidates=new[]{new JsonObject{["id"]="apple:long-real-id-one",["name"]="A"},new JsonObject{["id"]="apple:long-real-id-two",["name"]="B"}};
        var handler=new RankHandler();var result=await new PlanningAgent(new HttpClient(handler),secrets).RankAsync(new JsonObject(),candidates,default);
        Assert.Equal("apple:long-real-id-two",result.Array("preferredIds")![0]!.GetValue<string>());
        Assert.DoesNotContain("apple:long-real-id",handler.Input);Assert.Contains("p0",handler.Input);
        await Assert.ThrowsAsync<ServiceException>(()=>new PlanningAgent(new HttpClient(new RankHandler(true)),secrets).RankAsync(new JsonObject(),candidates,default));
    }
    sealed class RankHandler(bool invalid=false):HttpMessageHandler {
        public string Input="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            Input=await request.Content!.ReadAsStringAsync(ct);
            var output=new JsonObject{["preferredIds"]=invalid?new JsonArray("made-up"):new JsonArray("p1","p0"),["explanation"]="test"};
            var body=new JsonObject{["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["content"]=output.ToJsonString()}})};
            return new(HttpStatusCode.OK){Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json")};
        }
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    sealed class Handler(HttpStatusCode status=HttpStatusCode.OK,bool noTransit=false):HttpMessageHandler {
        public int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Count++;var json=noTransit?"{\"journeys\":[]}":File.ReadAllText(Path.Combine(TestSupport.RepositoryRoot(),"tests","fixtures","nsw-journey-test.json"));return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(json,Encoding.UTF8,"application/json")});}
    }
}
