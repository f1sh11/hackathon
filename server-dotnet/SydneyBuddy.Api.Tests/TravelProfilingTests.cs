using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SydneyBuddy.Api.Tests;

public sealed class TravelProfilingUnitTests
{
    [Fact]
    public void PlainDailyHabitCreatesOnlyAnUnconfirmedCandidateWhenModelOmitsIt()
    {
        var session=new PetSession();
        TravelProfiling.Apply(session,new JsonObject(),"通常每天最多走8000步。","test-habit");
        Assert.Single(session.Preferences);Assert.Equal("daily_steps",session.Preferences[0].Key);
        Assert.Equal("8000",session.Preferences[0].Value);Assert.Equal("awaiting_confirmation",session.Preferences[0].Status);
        Assert.Empty(session.ConfirmedDna);
    }
    [Theory]
    [InlineData("这次每天最多走8000步。")]
    [InlineData("通常每天走3000到8000步。")]
    [InlineData("通常每天走8000步，不过这次可以多走。")]
    public void AmbiguousOrTemporaryStepTextDoesNotInventAHabit(string text)
    {
        var session=new PetSession();TravelProfiling.Apply(session,new JsonObject(),text,"test-no-habit");
        Assert.Empty(session.Preferences);
    }

    internal static JsonObject Claim(string key,string value,string evidence,string scope="long_term",string condition="")=>new()
    { ["key"]=key,["value"]=value,["scope"]=scope,["evidence"]=evidence,["confidence"]=0.95,["condition"]=condition };
    [Fact]
    public void SunriseExceptionAndLateMorningsRemainSeparateUnconfirmedClaims()
    {
        var session=new PetSession();const string text="我讨厌早起，但为了看日出可以。";
        TravelProfiling.Apply(session,new JsonObject{["preferences"]=new JsonArray(Claim("normal_start_time","late",text),Claim("early_start_exception","sunrise",text))},text,"message-1");
        Assert.Equal(2,session.Preferences.Count);Assert.Empty(session.ConfirmedDna);
        Assert.All(session.Preferences,x=>{Assert.Equal("awaiting_confirmation",x.Status);Assert.Equal("user_statement",x.Source);Assert.Equal("message-1",x.SourceMessageId);Assert.Equal(text,x.Evidence);});
        Assert.Equal("late",session.Preferences[0].Value);Assert.Equal("sunrise",session.Preferences[1].Value);
    }
    [Fact]
    public void UnsupportedClaimsCannotBecomeFactsAndCompanionNeedsStayInTripScope()
    {
        var session=new PetSession();const string text="这次和妈妈一起，她不喜欢爬坡。";
        TravelProfiling.Apply(session,new JsonObject{["preferences"]=new JsonArray(Claim("hill_tolerance","low",text),Claim("normal_start_time","early","我喜欢早起"),Claim("api_key","leak",text))},text,"m");
        var fact=Assert.Single(session.Preferences);Assert.Equal("trip",fact.Scope);Assert.Equal("trip_only",fact.Status);Assert.Empty(TravelProfiling.Dna(session));
    }
    [Fact]
    public void InferencesStayOutOfPlanningAndLongTermDna()
    {
        var session=new PetSession();
        TravelProfiling.Apply(session,new JsonObject{["preferences"]=new JsonArray(Claim("daily_steps","2000","","inferred"))},"不知道走多少合适","m");
        Assert.Equal("ai_inference",Assert.Single(session.Preferences).Source);Assert.Empty(TravelProfiling.Dna(session));
        Assert.DoesNotContain("2000",JsonSerializer.Serialize(TravelProfiling.PlanningFields(session)));
    }
    [Fact]
    public void ConditionsPreventFalseConflictsButChangedValuesNeedReview()
    {
        var previous=new TravelPreference{Key="queue_tolerance_minutes",Value="20",MemoryId="old"};
        var normal=new TravelPreference{Key=previous.Key,Value="5"};TravelProfiling.SetConflicts(normal,[previous]);
        Assert.Equal(["old"],normal.ConflictingMemoryIds);
        var exception=new TravelPreference{Key=previous.Key,Value="60",Condition="only for a favourite museum"};TravelProfiling.SetConflicts(exception,[previous]);Assert.Empty(exception.ConflictingMemoryIds);
    }
    [Fact]
    public void ExplicitTripChoiceOverridesRecalledHabitWithoutMutatingDna()
    {
        var session=new PetSession{ConfirmedDna=[new(){Key="daily_steps",Value="10000"},new(){Key="normal_start_time",Value="late"}]};
        session.Fields["pace"]="约5000步";
        var planned=TravelProfiling.PlanningFields(session);
        Assert.Equal("约5000步",planned["pace"]);Assert.Contains("late",planned["notes"]);Assert.Equal("10000",session.ConfirmedDna[0].Value);
    }
    [Fact]
    public void AllTwelveDimensionsAreSupportedWithoutBecomingRequiredQuestions()
    {
        Assert.Equal(12,TravelProfiling.Keys.Values.Select(x=>x.Dimension).Distinct().Count());
        Assert.DoesNotContain("normal_start_time",PetQuestions.Required);Assert.DoesNotContain("nap_preference",PetQuestions.Required);
    }
    [Fact]
    public void RepeatingATripPreferenceAsAFutureHabitCreatesAConfirmationCandidate()
    {
        var session=new PetSession();const string trip="这次我想晚起";const string habit="以后我一般都想晚起";
        TravelProfiling.Apply(session,new JsonObject{["preferences"]=new JsonArray(Claim("normal_start_time","late",trip,"trip"))},trip,"trip-message");
        TravelProfiling.Apply(session,new JsonObject{["preferences"]=new JsonArray(Claim("normal_start_time","late",habit))},habit,"habit-message");
        var candidate=Assert.Single(session.Preferences,x=>x.Status=="awaiting_confirmation");
        Assert.Equal("long_term",candidate.Scope);Assert.Equal("habit-message",candidate.SourceMessageId);Assert.Empty(session.ConfirmedDna);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RevokedOrEditedMemoryCannotSurviveAsCopiedTripNotes(bool edited)
    {
        var previous=new TravelPreference{Key="daily_steps",Dimension="fitness",Value="5000",Status="confirmed",MemoryId="old"};
        var session=new PetSession{Preferences=[previous]};session.Fields["notes"]="饭点要准时；"+TravelProfiling.Label(previous);
        var records=new JsonArray();
        if(edited)records.Add(new JsonObject{["id"]="new",["profile"]=new JsonObject{["travelDna"]=JsonSerializer.SerializeToNode(new TravelPreference{Key="daily_steps",Value="7000"},JsonExtensions.Options)}});
        TravelProfiling.Refresh(session,records);
        Assert.Equal("饭点要准时",session.Fields["notes"]);Assert.Equal("revoked",previous.Status);
        var planned=TravelProfiling.PlanningFields(session);Assert.DoesNotContain("5000",JsonSerializer.Serialize(planned));
        if(edited)Assert.Equal("约7000步",planned["pace"]);
        else Assert.False(planned.ContainsKey("pace"));
    }
}

[Trait("Category","Integration")]
public sealed class TravelProfilingApiTests(ApiFixture fixture):IClassFixture<ApiFixture>
{
    private sealed class Model:IPetModel
    {
        public JsonObject Response {get;set;}=new(){["reply"]="那我们平时把早晨留给睡觉，日出留作值得破例的惊喜。",["fields"]=new JsonObject(),["preferences"]=new JsonArray()};
        public int Calls {get;private set;}
        public JsonArray Seen {get;private set;}=[];
        public Task<JsonObject> RespondAsync(PetSession session,string message,JsonArray memories,Func<string,Task> onReply,CancellationToken ct){Calls++;Seen=(JsonArray)memories.DeepClone();return Task.FromResult((JsonObject)Response.DeepClone());}
    }
    private static async Task<JsonObject> Create(HttpClient c)=>(await (await c.PostAsJsonAsync("pet/sessions",new{})).Content.ReadFromJsonAsync<JsonObject>())!;
    private static async Task<JsonObject> Turn(HttpClient c,string id,string message)
    {
        var result=await c.PostAsJsonAsync($"pet/sessions/{id}/turn",new{message,requestId=Guid.NewGuid().ToString()});
        var last=(await result.Content.ReadAsStringAsync()).Split('\n',StringSplitOptions.RemoveEmptyEntries).Last();
        var evt=JsonNode.Parse(last)!;Assert.Equal("done",evt.Str("type"));return evt.Object("session")!;
    }
    [Fact]
    public async Task NaturalReplySurvivesMissingFieldsAndAnUnknownAnswerDoesNotTriggerAPout()
    {
        var model=new Model();using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(model);}));
        using var c=factory.CreateClient();var id=(await Create(c)).Str("id");
        var session=await Turn(c,id,"我讨厌早起，但为了看日出可以。");
        Assert.Equal(model.Response.Str("reply"),session.Array("messages")!.Last()!.Str("text"));Assert.Equal("destination",session.Str("nextField"));
        model.Response["reply"]="没关系，可以先聊聊你想怎么度过这几天。";
        var unknown=await Turn(c,id,"不知道");Assert.Equal(2,model.Calls);Assert.Equal(model.Response.Str("reply"),unknown.Array("messages")!.Last()!.Str("text"));
    }
    [Fact]
    public async Task ConsentConflictRefreshCorrectionAndRevocationWorkAcrossSessions()
    {
        var model=new Model();const string late="我一般喜欢晚一点出发";const string early="我以后习惯早一点出发";
        using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(model);}));
        using var c=factory.CreateClient();var a=(await Create(c)).Str("id");var b=(await Create(c)).Str("id");
        model.Response["preferences"]=new JsonArray(TravelProfilingUnitTests.Claim("normal_start_time","late",late));
        var first=await Turn(c,a,late);var candidate=Assert.Single(first.Array("pendingPreferences")!);Assert.Empty((await c.GetFromJsonAsync<JsonArray>("memories"))!);
        model.Response["preferences"]=new JsonArray(TravelProfilingUnitTests.Claim("normal_start_time","early",early));
        var second=await Turn(c,b,early);var other=Assert.Single(second.Array("pendingPreferences")!);
        var body=new{candidateId=candidate!.Str("id"),consent=true,expectedConflicts=Array.Empty<string>()};
        (await c.PostAsJsonAsync($"pet/sessions/{a}/memory",body)).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync($"pet/sessions/{a}/memory",body)).EnsureSuccessStatusCode();Assert.Single((await c.GetFromJsonAsync<JsonArray>("memories"))!);
        var stale=await c.PostAsJsonAsync($"pet/sessions/{b}/memory",new{candidateId=other!.Str("id"),consent=true,expectedConflicts=Array.Empty<string>()});Assert.Equal(HttpStatusCode.UnprocessableEntity,stale.StatusCode);
        var refreshed=(await c.GetFromJsonAsync<JsonObject>($"pet/sessions/{b}"))!;
        var conflicted=Assert.Single(refreshed.Array("pendingPreferences")!);Assert.Equal("late",Assert.Single(conflicted!.Array("previousValues")!)!.GetValue<string>());
        (await c.PostAsJsonAsync($"pet/sessions/{b}/memory",new{candidateId=other!.Str("id"),consent=true,expectedConflicts=conflicted.Array("conflictingMemoryIds")})).EnsureSuccessStatusCode();
        var saved=Assert.Single((await c.GetFromJsonAsync<JsonArray>("memories"))!);Assert.Equal("early",saved!.Object("profile")!.Object("travelDna")!.Str("value"));
        var completed=await c.PostAsJsonAsync($"pet/sessions/{b}/profile/complete",new{});completed.EnsureSuccessStatusCode();
        Assert.True((await completed.Content.ReadFromJsonAsync<JsonObject>())!.Bool("dnaCompleted"));
        var fresh=await Create(c);Assert.Equal("early",Assert.Single(fresh.Array("travelDna")!)!.Str("value"));Assert.Empty(fresh.Object("tripContext")!);
        (await c.DeleteAsync("memories/"+saved.Str("id"))).EnsureSuccessStatusCode();
        var after=(await c.GetFromJsonAsync<JsonObject>($"pet/sessions/{a}"))!;Assert.Empty(after.Array("travelDna")!);
    }
}
