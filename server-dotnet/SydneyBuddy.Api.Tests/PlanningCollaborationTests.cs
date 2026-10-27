using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
namespace SydneyBuddy.Api.Tests;

public sealed class PlanningCollaborationTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"buddy-collaboration-"+Guid.NewGuid().ToString("N"));
    public PlanningCollaborationTests(){Directory.CreateDirectory(Path.Combine(root,"server"));File.Copy(Path.Combine(TestSupport.RepositoryRoot(),"server","places.json"),Path.Combine(root,"server","places.json"));}
    PlanningRunJournal Journal(string? run=null) {
        var accessor=new HttpContextAccessor{HttpContext=new DefaultHttpContext()};
        if(run is not null)accessor.HttpContext.Request.Headers[PlanningRunJournal.Header]=run;
        var result=new PlanningRunJournal(new(root),accessor,NullLogger<PlanningRunJournal>.Instance);result.Begin(accessor.HttpContext);return result;
    }
    static JsonObject Place(string id,double cost)=>new(){["id"]=id,["name"]=id,["lat"]=-33.86,["lng"]=151.21,["category"]="风景",["visitMinutes"]=20,["cost"]=cost,["openMinute"]=0,["closeMinute"]=1440};
    static JsonObject Proposal(string id)=>new(){["preferredIds"]=new JsonArray(id),["dayPlans"]=new JsonArray(new JsonObject{["dayIndex"]=0,["placeIds"]=new JsonArray(id)})};
    [Fact]public async Task RealBudgetReviewSendsFeedbackAndOnlyAcceptsRepairedCandidate() {
        var scheduler=new RouteScheduler(new RouteService(new FakeExternal()));var context=new PlanningContext("demo",new Dictionary<string,string>());var profile=TestSupport.Profile();profile["budget"]=20;
        var candidates=new[]{Place("expensive",80),Place("affordable",10)};profile=PlanValidator.Validate(profile,candidates);var before=profile.ToJsonString();var calls=0;var journal=Journal();
        var days=await PlanningCollaboration.RunAsync(feedback=>{calls++;if(calls==2)Assert.Contains("budget",feedback!.ToJsonString());return Task.FromResult(Proposal(calls==1?"expensive":"affordable"));},p=>scheduler.EvaluateProposalAsync(p,profile,candidates,context,default),()=>throw new Exception("Fallback should not run"),journal,default);
        Assert.Equal(2,calls);Assert.Equal("affordable",days[0]!.Array("stops")![0]!.Str("id"));Assert.Equal(before,profile.ToJsonString());
        var events=journal.Read(journal.CurrentId!).Array("events")!.OfType<JsonObject>().ToArray();
        Assert.Contains(events,e=>e.Str("status")=="needs_revision");Assert.Contains(events,e=>e.Str("action")=="revise_itinerary" && e.Int("attempt")==1);Assert.Equal("approved",events.Last().Str("status"));
    }
    [Fact]public async Task EvaluatorAllowsMoreThanFourFeasibleLowEnergyStops() {
        var scheduler=new RouteScheduler(new RouteService(new FakeExternal()));
        var points=Enumerable.Range(1,5).Select(i=>Place("short-"+i,0)).ToArray();
        var p=TestSupport.Profile();p["energy"]="低精力";p["maxSteps"]=8000;p["interests"]=new JsonArray("风景");
        p=PlanValidator.Validate(p,points);
        var proposal=new JsonObject{["dayPlans"]=new JsonArray(new JsonObject{["dayIndex"]=0,["placeIds"]=new JsonArray(points.Select(x=>(JsonNode)JsonValue.Create(x.Str("id"))!).ToArray())})};
        var (days,issues)=await scheduler.EvaluateProposalAsync(proposal,p,points,new PlanningContext("demo",new Dictionary<string,string>()),default);
        Assert.Empty(issues);Assert.Equal(5,days![0]!.Array("stops")!.Count);
        Assert.NotNull(days[0]!["stepEstimate"]);Assert.NotNull(days[0]!["mealSlots"]);
    }
    [Fact]public async Task ImpossibleRequestStopsAfterTwoRepairsAndNeverChangesConstraints() {
        var calls=0;var journal=Journal();
        await Assert.ThrowsAsync<ServiceException>(()=>PlanningCollaboration.RunAsync(_=>{calls++;return Task.FromResult(Proposal("A"));},_=>Task.FromResult<(JsonArray?,JsonArray)>((null,new(new JsonObject{["code"]="budget"}))),()=>throw new ServiceException("Cannot meet budget"),journal,default));
        Assert.Equal(3,calls);Assert.Contains("constraints_unsatisfied",journal.Read(journal.CurrentId!).ToJsonString());
    }
    [Fact]public async Task MissingProposalIsExplicitOptimizerFallbackNotFakeModelApproval() {
        var journal=Journal();var calls=0;
        await PlanningCollaboration.RunAsync(_=>{calls++;return Task.FromResult(new JsonObject());},_=>Task.FromResult<(JsonArray?,JsonArray)>((null,new(new JsonObject{["code"]="proposal_missing"}))),()=>Task.FromResult(new JsonArray()),journal,default);
        Assert.Equal(1,calls);Assert.Contains(journal.Read(journal.CurrentId!).Array("events")!.OfType<JsonObject>(),e=>e.Str("actor")=="RouteOptimizer");
    }
    [Fact]public async Task EvaluatorRejectsUnknownDuplicateAndMissingMustVisitWithoutRewritingInput() {
        var scheduler=new RouteScheduler(new RouteService(new FakeExternal()));var p=TestSupport.Profile();p["mustVisit"]=new JsonArray("required");var before=p.ToJsonString();var ctx=new PlanningContext("demo",new Dictionary<string,string>());
        var result=await scheduler.EvaluateProposalAsync(Proposal("unknown"),p,[Place("required",0)],ctx,default);
        Assert.Null(result.Days);Assert.Contains(result.Issues.OfType<JsonObject>(),x=>x.Str("code")=="unknown_place");Assert.Contains(result.Issues.OfType<JsonObject>(),x=>x.Str("code")=="must_visit_missing");Assert.Equal(before,p.ToJsonString());
    }
    [Fact]public void JournalPersistsAcrossRequestsWithSequenceAndRejectsPathTraversal() {
        var id=Guid.NewGuid().ToString();var first=Journal(id);first.Record("Planner","application","prepare","success");var second=Journal(id);second.Record("Reviewer","deterministic","validate","blocked");
        var rows=second.Read(id).Array("events")!.OfType<JsonObject>().ToArray();Assert.Equal(new[]{1,2},rows.Select(x=>x.Int("sequence")));Assert.All(rows,x=>Assert.Equal(id,x.Str("run_id")));Assert.Throws<ServiceException>(()=>second.Read("../../.secrets.json"));
        Assert.False(JsonNode.DeepEquals(rows[0]["timestamp"],rows[1]["timestamp"]));
    }
    [Fact]public async Task CancellationDoesNotStartFallbackOrAnotherModelCall() {
        using var cts=new CancellationTokenSource();cts.Cancel();var called=false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>PlanningCollaboration.RunAsync(_=>{called=true;return Task.FromResult(new JsonObject());},_=>throw new Exception(),()=>throw new Exception(),null,cts.Token));Assert.False(called);
    }
    [Fact]public async Task ModelReceivesRealEvaluatorFeedbackAndReturnsARepairedPlan() {
        File.WriteAllText(Path.Combine(root,".secrets.json"),"{\"NVIDIA_API_KEY\":\"test-token\"}");
        var handler=new RepairHandler();var journal=Journal();var agent=new PlanningAgent(new HttpClient(handler),new SecretStore(new(root)),memory:new FakeMemory(),journal:journal);
        var candidates=new[]{Place("expensive",80),Place("affordable",10)};var profile=TestSupport.Profile();profile["budget"]=20;profile=PlanValidator.Validate(profile,candidates);
        var scheduler=new RouteScheduler(new RouteService(new FakeExternal()));var context=new PlanningContext("demo",new Dictionary<string,string>());
        var days=await PlanningCollaboration.RunAsync(feedback=>agent.RankAsync(profile,candidates,default,feedback),p=>scheduler.EvaluateProposalAsync(p,profile,candidates,context,default),()=>throw new Exception("Unexpected fallback"),journal,default);
        Assert.Equal(2,handler.Calls);Assert.True(handler.FeedbackSeen);Assert.Equal("affordable",days[0]!.Array("stops")![0]!.Str("id"));
        var log=journal.Read(journal.CurrentId!).ToJsonString();Assert.DoesNotContain("test-token",log);Assert.DoesNotContain("Bearer",log);
    }
    sealed class RepairHandler:HttpMessageHandler {
        public int Calls;public bool FeedbackSeen;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            Calls++;var body=await request.Content!.ReadAsStringAsync(ct);if(Calls==2)FeedbackSeen=body.Contains("reviewFeedback")&&body.Contains("budget");
            var content=Proposal(Calls==1?"p0":"p1");content["explanation"]="Candidate choices revised against the review.";
            return new(HttpStatusCode.OK){Content=new StringContent(new JsonObject{["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["content"]=content.ToJsonString()}})}.ToJsonString(),Encoding.UTF8,"application/json")};
        }
    }
    [Fact]public async Task ModelUsesNightshiftEndpointAndReturnsMappedDayProposal() {
        File.WriteAllText(Path.Combine(root,".secrets.json"),"{\"AI_PROVIDER\":\"nightshift\",\"NIGHTSHIFT_BASE_URL\":\"http://127.0.0.1:8088/v1\",\"NIGHTSHIFT_API_KEY\":\"test-token\",\"NIGHTSHIFT_MODEL\":\"meterless/auto\"}");
        var handler=new ProviderHandler();var agent=new PlanningAgent(new HttpClient(handler),new SecretStore(new(root)));
        Assert.True(agent.Configured);
        var result=await agent.RankAsync(TestSupport.Profile(),[Place("canonical-place",10)],default,new(){["issues"]=new JsonArray(new JsonObject{["code"]="budget"})});
        Assert.Equal("canonical-place",result.Array("dayPlans")![0]!.Array("placeIds")![0]!.GetValue<string>());Assert.Contains("reviewFeedback",handler.Body);Assert.Contains("meterless/auto",handler.Body);Assert.Equal("127.0.0.1",handler.Host);Assert.DoesNotContain("response_format",handler.Body);
    }
    [Fact]public void NightshiftConfigurationNeverFallsBackToNvidiaAndRejectsUnsafeRemoteHttp() {
        Assert.Throws<ServiceException>(()=>ModelProviderSettings.Resolve(new Dictionary<string,string>{{"AI_PROVIDER","nightshift"},{"NVIDIA_API_KEY","test"}}));
        Assert.Throws<ServiceException>(()=>ModelProviderSettings.Resolve(new Dictionary<string,string>{{"AI_PROVIDER","nightshift"},{"NIGHTSHIFT_BASE_URL","http://remote.example/v1"},{"NIGHTSHIFT_API_KEY","test"}}));
    }
    sealed class ProviderHandler:HttpMessageHandler {
        public string Body="",Host="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Body=await request.Content!.ReadAsStringAsync(ct);Host=request.RequestUri!.Host;var content=new JsonObject{["preferredIds"]=new JsonArray("p0"),["dayPlans"]=new JsonArray(new JsonObject{["dayIndex"]=0,["placeIds"]=new JsonArray("p0")}),["explanation"]="A verified candidate"};return new(HttpStatusCode.OK){Content=new StringContent(new JsonObject{["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["content"]=content.ToJsonString()}})}.ToJsonString(),Encoding.UTF8,"application/json")};}
    }
    public void Dispose(){Directory.Delete(root,true);}
}
