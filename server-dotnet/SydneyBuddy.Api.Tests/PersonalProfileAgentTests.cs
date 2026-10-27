using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SydneyBuddy.Api.Tests;

public sealed class PersonalProfileAgentTests
{
    [Fact]
    public async Task RealModelTransportUsesPersonalSkillAndExcludesTripQuestionnaireContext()
    {
        var root=Path.Combine(Path.GetTempPath(),"personal-agent-model-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var secrets=new SecretStore(new ProjectPaths(root));
            secrets.Save(new Dictionary<string,string>{{"NVIDIA_API_KEY","fixture-key"}});
            using var handler=new ModelTransport();using var client=new HttpClient(handler);
            var session=new PetSession{ResponseLanguage="en"};PersonalProfileAgent.Enable(session,new());
            // Even supplied trip data is not part of the personal agent's model context.
            session.Fields["destination"]="Sydney";
            var result=await new PetModel(client,secrets).RespondAsync(session,"Hello",new(),_=>Task.CompletedTask,default);
            Assert.Equal(3,PersonalProfileAgent.Replies(result).Length);
            var messages=handler.Payload!.Array("messages")!;
            Assert.Contains(PersonalProfileAgent.Prompt,messages[0]!.Str("content"));
            Assert.DoesNotContain(KokoDialogue.Prompt,messages[0]!.Str("content"));
            var context=messages[1]!.Str("content");
            Assert.Contains("personal_profile",context);Assert.DoesNotContain("tripFields",context);
            Assert.DoesNotContain("missingImportantFields",context);Assert.DoesNotContain("destination",context);
        } finally {Directory.Delete(root,true);}
    }

    private sealed class ModelTransport:HttpMessageHandler
    {
        public JsonObject? Payload {get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Payload=JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            var content=PersonalProfileAgentApiTests.Response().ToJsonString();
            var chunk=new JsonObject{["choices"]=new JsonArray(new JsonObject{["delta"]=new JsonObject{["content"]=content}})};
            return new(HttpStatusCode.OK){Content=new StringContent("data: "+chunk.ToJsonString()+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
        }
    }

    [Fact]
    public void PersonalChoicesNeedConfirmationAndCannotInventANumericFitnessLimit()
    {
        var session=new PetSession();PersonalProfileAgent.Enable(session,new());
        session.CurrentChoice=new("question","What kind of walks do you usually enjoy?","option","Short scenic walks");
        TravelProfiling.Apply(session,new JsonObject{["preferences"]=new JsonArray(
            TravelProfilingUnitTests.Claim("interests","scenic walks","Short scenic walks"),
            TravelProfilingUnitTests.Claim("daily_steps","6000","Short scenic walks"))},"Short scenic walks","message");
        var candidate=Assert.Single(session.Preferences);
        Assert.Equal("interests",candidate.Key);Assert.Equal("long_term",candidate.Scope);
        Assert.Equal("awaiting_confirmation",candidate.Status);Assert.Equal(session.CurrentChoice,candidate.Choice);
        Assert.Empty(session.ConfirmedDna);
    }

    [Fact]
    public void TemporaryFeelingAndInferenceDoNotBecomePersonalMemoryCandidates()
    {
        var session=new PetSession();PersonalProfileAgent.Enable(session,new());
        const string message="I'm tired and want a rest today.";
        TravelProfiling.Apply(session,new JsonObject{["preferences"]=new JsonArray(
            TravelProfilingUnitTests.Claim("rhythm","relaxed",message),
            TravelProfilingUnitTests.Claim("daily_steps","3000","","inferred"))},message,"message");
        Assert.Empty(session.Preferences);
    }
}

[Trait("Category","Integration")]
public sealed class PersonalProfileAgentApiTests(ApiFixture fixture):IClassFixture<ApiFixture>
{
    internal static JsonObject Response(string reply="Hi, I'm Koko. What do you usually enjoy on a day off?")=>new()
    {
        ["reply"]=reply,["fields"]=new JsonObject(),["preferences"]=new JsonArray(),["mood"]="curious",
        ["quickReplies"]=new JsonArray(
            new JsonObject{["text"]="Quiet places",["intent"]="respond"},
            new JsonObject{["text"]="Something active",["intent"]="respond"},
            new JsonObject{["text"]="It depends on my mood",["intent"]="respond"})
    };

    private sealed class Model:IPetModel
    {
        public JsonObject Result {get;set;}=Response();
        public int Calls {get;private set;}
        public string Purpose {get;private set;}="";
        public JsonArray Memories {get;private set;}=new();
        public Task<JsonObject> RespondAsync(PetSession session,string message,JsonArray memories,Func<string,Task> onReply,CancellationToken ct)
        {Calls++;Purpose=session.Purpose;Memories=(JsonArray)memories.DeepClone();return Task.FromResult((JsonObject)Result.DeepClone());}
    }

    private static async Task<JsonObject> Create(HttpClient client)
    {
        var response=await client.PostAsJsonAsync("pet/sessions",new{purpose="personal_profile",profilingVersion=3,language="en"});
        response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }
    private static JsonObject Last(JsonObject session)=>session.Array("messages")!.Last()!.AsObject();
    private static string[] OptionIds(JsonObject session)=>Last(session).Array("quickReplies")!.Select(x=>x!.Str("id")).ToArray();
    private static async Task<JsonObject> Event(HttpClient client,string id,object body)
    {
        var response=await client.PostAsJsonAsync($"pet/sessions/{id}/turn",body);response.EnsureSuccessStatusCode();
        return JsonNode.Parse((await response.Content.ReadAsStringAsync()).Split('\n',StringSplitOptions.RemoveEmptyEntries).Last())!.AsObject();
    }

    [Fact]
    public async Task OpeningChoiceFreeTextRetryAndResumeKeepThreeDynamicOptions()
    {
        var model=new Model();using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(model);}));
        using var client=factory.CreateClient();var opening=await Create(client);var id=opening.Str("id");
        Assert.Equal("personal_profile",model.Purpose);Assert.Equal(1,model.Calls);Assert.Equal(3,OptionIds(opening).Length);
        var first=Last(opening);var selected=first.Array("quickReplies")![0]!;
        model.Result=Response("What makes somewhere feel quiet enough for you?");
        model.Result["quickReplies"]=new JsonArray(
            new JsonObject{["text"]="Space away from crowds",["intent"]="respond"},
            new JsonObject{["text"]="A calm place to sit",["intent"]="respond"},
            new JsonObject{["text"]="Let's talk about another preference",["intent"]="skip"});
        var chosen=await Event(client,id,new{message=selected.Str("text"),questionId=first.Str("id"),optionId=selected.Str("id"),requestId=Guid.NewGuid().ToString(),language="en"});
        Assert.Equal("done",chosen.Str("type"));var afterChoice=chosen.Object("session")!;
        Assert.Equal(model.Result.Str("reply"),Last(afterChoice).Str("text"));Assert.Equal(3,OptionIds(afterChoice).Length);
        Assert.DoesNotContain(OptionIds(afterChoice)[0],OptionIds(opening));
        var evidence=afterChoice.Array("messages")![1]!.Object("choice")!;
        Assert.Equal(first.Str("text"),evidence.Str("questionText"));Assert.Equal(selected.Str("text"),evidence.Str("optionText"));

        var body=new{message="I usually like a quiet morning and an active afternoon.",requestId=Guid.NewGuid().ToString(),language="en"};
        model.Result=Response("Would you normally leave the morning open, then head out later?");
        var typed=(await Event(client,id,body)).Object("session")!;Assert.Equal(3,OptionIds(typed).Length);
        Assert.Equal(3,model.Calls);
        var repeated=(await Event(client,id,body)).Object("session")!;
        Assert.Equal(OptionIds(typed),OptionIds(repeated));Assert.Equal(3,model.Calls);
        var restored=(await client.GetFromJsonAsync<JsonObject>($"pet/sessions/{id}"))!;
        Assert.Equal(OptionIds(typed),OptionIds(restored));Assert.Equal("personal_profile",restored.Str("purpose"));
        Assert.Empty(restored.Object("fields")!);Assert.Empty(restored.Array("missingImportantFields")!);

        var stale=await Event(client,id,new{message=selected.Str("text"),questionId=first.Str("id"),optionId=selected.Str("id"),requestId=Guid.NewGuid().ToString()});
        // The exact earlier choice is recognised as an idempotent replay, not a new answer.
        Assert.Equal("done",stale.Str("type"));Assert.Equal(3,model.Calls);
        var unknown=await Event(client,id,new{message="a changed answer",questionId=first.Str("id"),optionId="unknown",requestId=Guid.NewGuid().ToString()});
        Assert.Equal("error",unknown.Str("type"));Assert.Equal(3,model.Calls);
    }

    [Fact]
    public async Task InvalidChoiceSetPreservesThePreviousTurnAndCanBeRetried()
    {
        var model=new Model();using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(model);}));
        using var client=factory.CreateClient();var initial=await Create(client);var id=initial.Str("id");
        var body=new{message="I enjoy art.",requestId=Guid.NewGuid().ToString(),language="en"};
        model.Result=Response();model.Result.Array("quickReplies")!.RemoveAt(2);
        model.Result["preferences"]=new JsonArray(TravelProfilingUnitTests.Claim("interests","art",body.message));
        var invalid=await Event(client,id,body);Assert.Equal("error",invalid.Str("type"));
        var preserved=(await client.GetFromJsonAsync<JsonObject>($"pet/sessions/{id}"))!;
        Assert.Single(preserved.Array("messages")!);Assert.Empty(preserved.Array("pendingPreferences")!);
        Assert.Equal(OptionIds(initial),OptionIds(preserved));
        model.Result=Response("What kind of art would you make time to see?");
        var retried=await Event(client,id,body);Assert.Equal("done",retried.Str("type"));
        Assert.Equal(3,retried.Object("session")!.Array("messages")!.Count);
    }

    [Fact]
    public async Task PersonalModeCannotCollectTripFieldsOrUseTripControls()
    {
        var model=new Model();using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(model);}));
        using var client=factory.CreateClient();var initial=await Create(client);var id=initial.Str("id");
        model.Result=Response("We can prepare the trip next. What do you usually enjoy exploring?");
        model.Result["fields"]=new JsonObject{["destination"]="Sydney",["budget"]="100",["date"]="2026-11-01"};
        var final=(await Event(client,id,new{message="Sydney on 1 November, AUD 100 per person per day.",requestId=Guid.NewGuid().ToString()})).Object("session")!;
        Assert.Empty(final.Object("fields")!);Assert.Empty(final.Object("planningFields")!);
        Assert.Empty(final.Array("missingImportantFields")!);Assert.Equal("",final.Str("nextField"));Assert.Empty(final.Array("options")!);
        var patch=await client.PatchAsJsonAsync($"pet/sessions/{id}",new{fields=new{destination="Sydney"}});
        Assert.Equal(HttpStatusCode.UnprocessableEntity,patch.StatusCode);
        var direct=await Event(client,id,new{message="Sydney",field="destination",value="Sydney",requestId=Guid.NewGuid().ToString()});
        Assert.Equal("error",direct.Str("type"));Assert.Equal(2,model.Calls);
    }

    [Fact]
    public async Task ContextualPersonalPreferenceSavesOnlyAfterConsentAndIsRecalled()
    {
        var model=new Model();using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(model);}));
        using var client=factory.CreateClient();var initial=await Create(client);var id=initial.Str("id");
        var before=(await client.GetFromJsonAsync<JsonArray>("memories"))!.Count;
        var question=Last(initial);var option=question.Array("quickReplies")![0]!;
        model.Result=Response("What makes a place feel comfortably quiet for you?");
        model.Result["preferences"]=new JsonArray(TravelProfilingUnitTests.Claim("crowd_preference","quiet places",option.Str("text")));
        var final=(await Event(client,id,new{message=option.Str("text"),questionId=question.Str("id"),optionId=option.Str("id"),requestId=Guid.NewGuid().ToString()})).Object("session")!;
        var candidate=Assert.Single(final.Array("pendingPreferences")!)!;
        Assert.Equal("long_term",candidate.Str("scope"));Assert.Equal("contextual_choice",candidate.Str("source"));
        Assert.Equal(before,(await client.GetFromJsonAsync<JsonArray>("memories"))!.Count);
        var consent=new{candidateId=candidate.Str("id"),consent=true,expectedConflicts=Array.Empty<string>()};
        (await client.PostAsJsonAsync($"pet/sessions/{id}/memory",consent)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"pet/sessions/{id}/memory",consent)).EnsureSuccessStatusCode();
        Assert.Equal(before+1,(await client.GetFromJsonAsync<JsonArray>("memories"))!.Count);
        await Create(client);Assert.Contains(model.Memories,x=>x!.Object("profile")?.Object("travelDna")?.Str("value")=="quiet places");
    }

    [Fact]
    public async Task RefusalAndCompletedIntroductionNeverForcePermanentStorageOrATripQuestion()
    {
        var model=new Model();using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(model);}));
        using var client=factory.CreateClient();var initial=await Create(client);var id=initial.Str("id");
        const string text="I like museums, but don't save that.";
        model.Result=Response("We can leave that out of your saved profile. Shall we finish here?");
        model.Result["preferences"]=new JsonArray(TravelProfilingUnitTests.Claim("interests","museums",text));
        model.Result["readyForDraft"]=true;
        var final=(await Event(client,id,new{message=text,requestId=Guid.NewGuid().ToString()})).Object("session")!;
        Assert.Empty(final.Array("pendingPreferences")!);Assert.Equal("profile_ready",final.Object("dialogue")!.Str("phase"));
        Assert.Equal(3,OptionIds(final).Length);Assert.Equal("",final.Str("nextField"));
        model.Result=Response("Of course. What sort of places usually catch your attention?");
        var continued=(await Event(client,id,new{message="I'd like to keep chatting.",requestId=Guid.NewGuid().ToString()})).Object("session")!;
        Assert.Equal(3,OptionIds(continued).Length);Assert.Empty(continued.Object("fields")!);
    }
}
