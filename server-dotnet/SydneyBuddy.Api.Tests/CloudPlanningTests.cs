using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace SydneyBuddy.Api.Tests;

public sealed class CloudPlanningTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"cloud-planning-"+Guid.NewGuid().ToString("N"));
    public CloudPlanningTests(){Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,".secrets.json"),"{\"NVIDIA_API_KEY\":\"test-fixture-not-a-real-key\"}");}
    SecretStore Secrets()=>new(new ProjectPaths(root));
    internal static PlanningAgentSettings Settings(bool enabled=true)=>new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["PlanningAgent:Provider"]="meterless-cloud",["PlanningAgent:AllowCloudModelCalls"]=enabled.ToString()}).Build());
    [Fact]public async Task CloudAgentUsesReviewedSkillConfirmedMemoryWorldContextAndSeparateStays(){
        var settings=Settings();var handler=new CloudHandler();var memory=new RecordingMemory();
        var cloud=new CloudPlanningModel(new HttpClient(handler),Secrets(),settings);
        var agent=new PlanningAgent(new HttpClient(new RejectHandler()),Secrets(),settings,null,memory,cloud);
        var p=TestSupport.Profile();p["mustVisit"]=new JsonArray("osm:node:10");p["language"]="en";p["indoorPreference"]="preferIndoor";
        var place=new JsonObject{["id"]="osm:node:10",["name"]="Museum",["category"]="文化",["indoor"]=true};
        var stay=new JsonObject{["id"]="osm:node:20",["name"]="Hotel",["category"]="住宿",["nightlyRateAud"]=200};
        var weather=new JsonObject{["days"]=new JsonArray(new JsonObject{["date"]=p.Str("date"),["rainy"]=true,["available"]=true})};
        var result=await agent.ProposeAsync(p,[place],weather,[stay],default);
        Assert.True(agent.Configured);Assert.Equal("meterless-cloud",result.Str("provider"));Assert.Equal("Meterless H-MEM",result.Str("memoryEngine"));Assert.Equal(1,result.Int("confirmedMemoryCount"));
        Assert.Equal(new[]{"query"},memory.Actions);Assert.Equal("Bearer test-fixture-not-a-real-key",handler.Auth);Assert.Equal("integrate.api.nvidia.com",handler.Host);
        var messages=handler.Payload!.Array("messages")!;Assert.Contains("Weather × activity setting × interests",messages[0]!.Str("content"));
        var evidence=JsonNode.Parse(messages[1]!.Str("content"))!.AsObject();Assert.Equal("User confirmed: likes art.",evidence.Str("confirmedMemoryContext"));Assert.Equal("proposing",evidence.Object("worldState")!.Str("phase"));Assert.Equal("not_saved",evidence.Object("worldState")!.Str("persistence"));Assert.Equal(weather.ToJsonString(),evidence["weather"]!.ToJsonString());Assert.Single(evidence.Array("accommodations")!);Assert.DoesNotContain("osm:node:20",messages[1]!.Str("content"));
        Assert.Equal("osm:node:10",Assert.Single(result.Array("preferredIds")!)!.ToString());Assert.Equal("osm:node:20",Assert.Single(result.Array("accommodationIds")!)!.ToString());
    }
    [Fact]public async Task MissingKeyFailsWithoutAnyNetworkCall(){File.Delete(Path.Combine(root,".secrets.json"));var model=new CloudPlanningModel(new HttpClient(new RejectHandler()),Secrets(),Settings());Assert.False(model.Configured);var error=await Assert.ThrowsAsync<ServiceException>(()=>model.CompleteAsync("skill",new(),default));Assert.Contains("金鑰",error.Message);}
    [Fact]public async Task ModelOnlyPermissionIsRequiredWithoutEnablingOtherPaidServices(){var model=new CloudPlanningModel(new HttpClient(new RejectHandler()),Secrets(),Settings(false));Assert.False(model.Configured);await Assert.ThrowsAsync<ServiceException>(()=>model.CompleteAsync("skill",new(),default));Assert.False(new PaidServicePolicy(new ConfigurationBuilder().Build()).Allowed);}
    [Fact]public async Task ProviderErrorDoesNotEchoBodyOrKey(){var model=new CloudPlanningModel(new HttpClient(new CloudHandler{Status=HttpStatusCode.Unauthorized}),Secrets(),Settings());var error=await Assert.ThrowsAsync<ServiceException>(()=>model.CompleteAsync("skill",new(),default));Assert.Contains("401",error.Message);Assert.DoesNotContain("sensitive-provider-body",error.Message);Assert.DoesNotContain("test-fixture",error.Message);}
    [Fact]public async Task UnrecognizedCloudPlaceFailsClosed(){var settings=Settings();var model=new CloudPlanningModel(new HttpClient(new CloudHandler{Unknown=true}),Secrets(),settings);var agent=new PlanningAgent(new HttpClient(new RejectHandler()),Secrets(),settings,null,new RecordingMemory(),model);var p=TestSupport.Profile();p["mustVisit"]=new JsonArray();await Assert.ThrowsAsync<ServiceException>(()=>agent.ProposeAsync(p,[new(){["id"]="osm:node:10",["name"]="Museum"}],new(),[],default));}
    [Fact]public async Task ChineseSkillIsSentForTraditionalChineseProfile(){var h=new CloudHandler();var s=Settings();var a=new PlanningAgent(new HttpClient(new RejectHandler()),Secrets(),s,null,new RecordingMemory(),new CloudPlanningModel(new HttpClient(h),Secrets(),s));var p=TestSupport.Profile();p["mustVisit"]=new JsonArray();p["language"]="zh-Hant";await a.ProposeAsync(p,[new(){["id"]="osm:node:10",["name"]="Museum"}],new(),[],default);Assert.Contains("天氣 × 室內外",h.Payload!.Array("messages")![0]!.Str("content"));}

    internal sealed class CloudHandler:HttpMessageHandler {
        public JsonObject? Payload;public string? Auth,Host;public HttpStatusCode Status=HttpStatusCode.OK;public bool Unknown;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            Host=request.RequestUri!.Host;Auth=request.Headers.Authorization?.ToString();Payload=JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            if(Status!=HttpStatusCode.OK)return new(Status){Content=new StringContent("sensitive-provider-body",Encoding.UTF8,"application/json")};
            var evidence=JsonNode.Parse(Payload.Array("messages")![1]!.Str("content"))!.AsObject();
            var ids=evidence.Array("candidates")!.OfType<JsonObject>().Take(60).Select(p=>(JsonNode)JsonValue.Create(p.Str("id"))!).ToArray();
            var stays=evidence.Array("accommodations")!.OfType<JsonObject>().Take(1).Select(p=>(JsonNode)JsonValue.Create(p.Str("id"))!).ToArray();
            var output=new JsonObject{["preferredIds"]=Unknown?new JsonArray("invented"):new JsonArray(ids),["accommodationIds"]=new JsonArray(stays),["dayPlans"]=new JsonArray(),["explanation"]="Interest and weather aware proposal.",["outstandingChecks"]=new JsonArray("Hours and availability need confirmation.")};
            return new(Status){Content=new StringContent(Status==HttpStatusCode.OK?new JsonObject{["choices"]=new JsonArray(new JsonObject{["message"]=new JsonObject{["content"]=output.ToJsonString()}})}.ToJsonString():"sensitive-provider-body",Encoding.UTF8,"application/json")};
        }
    }
    sealed class RejectHandler:HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>throw new InvalidOperationException("Unexpected network call.");}
    sealed class RecordingMemory:IMemoryService {
        public List<string> Actions=[];
        public Task<JsonObject> RunAsync(string action,JsonObject input,CancellationToken ct=default){Actions.Add(action);return Task.FromResult(new JsonObject{["context"]="User confirmed: likes art.",["memories"]=new JsonArray(new JsonObject{["source"]="user_confirmed"})});}
    }
    public void Dispose()=>Directory.Delete(root,true);
}
