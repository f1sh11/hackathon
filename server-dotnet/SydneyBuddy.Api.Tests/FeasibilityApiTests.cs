using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace SydneyBuddy.Api.Tests;

[Trait("Category","Integration")]
public sealed class FeasibilityApiTests: IClassFixture<ApiFixture>,IDisposable {
    readonly WebApplicationFactory<Program> host;readonly HttpClient client;
    public FeasibilityApiTests(ApiFixture fixture) {
        host=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IFeasibilityReasoner>();s.AddSingleton<IFeasibilityReasoner>(new TestFeasibilityReasoner());}));
        client=host.CreateClient(new(){BaseAddress=new Uri("http://localhost")});
    }
    async Task<JsonObject> Review(JsonObject input){var response=await client.PostAsJsonAsync("/agents/feasibility/reviews",input);response.EnsureSuccessStatusCode();return(await response.Content.ReadFromJsonAsync<JsonObject>())!;}
    [Fact]public async Task ReviewConfirmAndRetrySaveExactlyOneReviewedSnapshot() {
        var draft=FeasibilityAgentTests.Draft();var result=await Review(FeasibilityAgentTests.Request(draft));Assert.Equal("pass",result.Str("decision"));
        var path="/agents/feasibility/reviews/"+result.Str("reviewId")+"/confirm";
        var response=await client.PostAsJsonAsync(path,new{planRevision=1,requirementsRevision=1,expectedSavedRevision=0});response.EnsureSuccessStatusCode();var saved=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var retry=await client.PostAsJsonAsync(path,new{planRevision=1,requirementsRevision=1,expectedSavedRevision=0});retry.EnsureSuccessStatusCode();var repeated=(await retry.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(saved.Str("id"),repeated.Str("id"));Assert.Equal(1,repeated.Int("revision"));Assert.True(JsonNode.DeepEquals(draft["days"],saved["days"]));
    }
    [Fact]public async Task BudgetFailureCannotBeConfirmedOrSpoofedByClient() {
        var draft=FeasibilityAgentTests.Draft();draft.Object("profile")!["budget"]=1;var result=await Review(FeasibilityAgentTests.Request(draft));Assert.Equal("revise",result.Str("decision"));
        var path="/agents/feasibility/reviews/"+result.Str("reviewId")+"/confirm";
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.PostAsJsonAsync(path,new{planRevision=1,requirementsRevision=1,expectedSavedRevision=0})).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.PostAsJsonAsync(path,new{planRevision=1,requirementsRevision=1,expectedSavedRevision=0,canConfirm=true})).StatusCode);
    }
    [Fact]public async Task ChangedDraftInvalidatesOldApprovalAndDoesNotSaveIt() {
        var request=FeasibilityAgentTests.Request();var result=await Review(request);request["planRevision"]=2;request.Object("trip")!["title"]="new draft";await Review(request);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/agents/feasibility/reviews/"+result.Str("reviewId")+"/confirm",new{planRevision=1,requirementsRevision=1,expectedSavedRevision=0})).StatusCode);
    }
    [Fact]public async Task ReviewReadDoesNotExposePrivateInputEnvelope() {
        var result=await Review(FeasibilityAgentTests.Request());var read=(await client.GetFromJsonAsync<JsonObject>("/agents/feasibility/reviews/"+result.Str("reviewId")))!;
        Assert.False(read.ContainsKey("trip"));Assert.False(read.ContainsKey("requirements"));Assert.True(read.ContainsKey("issues"));
    }
    [Fact]public async Task ConfirmationCannotSwapReviewedTrip() {
        var result=await Review(FeasibilityAgentTests.Request());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.PostAsJsonAsync("/agents/feasibility/reviews/"+result.Str("reviewId")+"/confirm",new{planRevision=1,requirementsRevision=1,expectedSavedRevision=0,trip=new{title="replacement"}})).StatusCode);
    }
    [Fact]public async Task SavedTripReferenceAvoidsSendingUndoHistoryAndChecksBaseRevision() {
        var savedResponse=await client.PostAsJsonAsync("/trips",new{trip=FeasibilityAgentTests.Draft(),expectedRevision=0});savedResponse.EnsureSuccessStatusCode();var saved=(await savedResponse.Content.ReadFromJsonAsync<JsonObject>())!;
        var q=new JsonObject{["planId"]=Guid.NewGuid().ToString(),["planRevision"]=1,["requirementsRevision"]=1,["tripId"]=saved.Str("id"),["expectedSavedRevision"]=1};
        var result=await Review(q);Assert.True(result.Bool("canConfirm"));q["expectedSavedRevision"]=0;
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/agents/feasibility/reviews",q)).StatusCode);
    }
    [Fact]public async Task StaleReviewErrorIsEnglish(){var q=FeasibilityAgentTests.Request();await Review(q);q.Object("trip")!["title"]="Changed without a revision";var response=await client.PostAsJsonAsync("/agents/feasibility/reviews",q);Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);var error=(await response.Content.ReadFromJsonAsync<JsonObject>())!.Str("error");Assert.Contains("changed",error);Assert.DoesNotContain(error,c=>c is >= '\u3400' and <= '\u9fff');}
    public void Dispose(){client.Dispose();host.Dispose();}
}
