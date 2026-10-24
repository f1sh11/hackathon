using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SydneyBuddy.Api.Tests;

public sealed class KokoDialogueUnitTests
{
    [Fact]
    public void OptionalNameCountsAsOneRoundAndDoesNotRequireModelOrTyping()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        var hello=Assert.Single(s.Messages);Assert.Contains("Koko",hello.Text);
        Assert.Equal(3,hello.QuickReplies!.Length);Assert.Empty(s.Fields);Assert.Empty(s.Preferences);
        var skip=Assert.Single(hello.QuickReplies,x=>x.Intent=="skip_name");
        KokoDialogue.ResolveChoice(s,Selection(hello,skip));
        var response=KokoDialogue.DirectResponse(s,skip.Text,skip)!;
        var reply=KokoDialogue.CompleteTurn(s,response,response.Str("reply"),"curious");
        Assert.Equal(1,s.Dialogue!.ResponseCount);Assert.Equal("exploring",s.Dialogue.Phase);
        Assert.Equal(3,reply.QuickReplies!.Length);Assert.Empty(s.Dialogue.Nickname);
    }
    [Fact]
    public void OnlyExplicitNicknameIsReusedAndReturningUserIsNotIntroducedAgain()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        var response=KokoDialogue.DirectResponse(s,"叫我 小鱼",null)!;
        KokoDialogue.CompleteTurn(s,response,response.Str("reply"),"curious");Assert.Equal("小鱼",s.Dialogue!.Nickname);
        var returning=new PetSession();KokoDialogue.Enable(returning,new(){["profilingVersion"]=2,["nickname"]=s.Dialogue.Nickname,["hasMetKoko"]=true});
        Assert.Contains("小鱼",returning.Messages[0].Text);Assert.DoesNotContain("怎么称呼",returning.Messages[0].Text);Assert.DoesNotContain("我叫",returning.Messages[0].Text);
        Assert.Empty(returning.Preferences);
    }
    [Fact]
    public void FourthAnswerForcesInspirationWithoutCompletingMemoryOrFormalItinerary()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        for(var i=0;i<4;i++) {
            var result=new JsonObject{["reply"]="还想了解你的起床时间？",["ideas"]=new JsonArray("如果路线上有合适的展览，留一小段时间看看。")};
            var reply=KokoDialogue.CompleteTurn(s,result,result.Str("reply"),"curious");
            if(i<3)Assert.Null(s.Dialogue!.Inspiration);
            else {Assert.DoesNotContain("起床",reply.Text);Assert.DoesNotContain("？",reply.Text);Assert.Equal(3,reply.QuickReplies!.Length);}
        }
        Assert.Equal(4,s.Dialogue!.ResponseCount);Assert.Equal("inspiration",s.Dialogue.Phase);
        Assert.False(s.Dialogue.Inspiration!.PlacesVerified);Assert.Equal("inspiration",s.Dialogue.Inspiration.Status);
        Assert.Contains("目的地待定",s.Dialogue.Inspiration.Title);Assert.Equal(PetQuestions.Required,TravelProfiling.Missing(s));Assert.False(s.DnaCompleted);
        Assert.Empty(s.ConfirmedDna);Assert.Empty(s.Fields);
    }
    [Theory]
    [InlineData("快点")]
    [InlineData("别问了")]
    [InlineData("直接安排")]
    [InlineData("能不能快点，先给我看看")]
    public void ExpediteEndsImmediatelyWithUnknownsIntact(string text)
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        var result=KokoDialogue.DirectResponse(s,text,null)!;
        var reply=KokoDialogue.CompleteTurn(s,result,result.Str("reply"),"curious");
        Assert.NotNull(s.Dialogue!.Inspiration);Assert.Equal(1,s.Dialogue.ResponseCount);Assert.Empty(s.Fields);Assert.DoesNotContain("？",reply.Text);
    }
    [Fact]
    public void RefiningAfterCompletionDoesNotRestartTheInterviewOrReplaceAnAnswerWithBoilerplate()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        s.Dialogue!.ResponseCount=3;
        KokoDialogue.CompleteTurn(s,new(),"结束画像","curious");
        var reply=KokoDialogue.CompleteTurn(s,new(){["ideas"]=new JsonArray("如果有适合的室内活动，就优先留给下雨的时段。")},"这一段可以先改成室内备选。","curious");
        Assert.Equal("这一段可以先改成室内备选。",reply.Text);Assert.Equal(4,s.Dialogue.ResponseCount);
        Assert.Contains("室内",Assert.Single(s.Dialogue.Inspiration!.Ideas));
    }
    [Fact]
    public void ContextualClickKeepsFullSceneAndCannotBecomeGlobalFitnessOrPace()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        var option=new PetQuickReply("on-route","顺路再去");
        var question=new PetMessage("scene-1","assistant","这趟如果路线上有合适的漫画店，咱们进去看看？",QuickReplies:[option]);s.Messages.Add(question);
        KokoDialogue.ResolveChoice(s,Selection(question,option));
        var result=new JsonObject{["preferences"]=new JsonArray(
            TravelProfilingUnitTests.Claim("interests","看看漫画店",option.Text),
            TravelProfilingUnitTests.Claim("daily_steps","5000",option.Text),
            TravelProfilingUnitTests.Claim("rhythm","轻松随意",option.Text))};
        TravelProfiling.Apply(s,result,option.Text,"click-message");
        var fact=Assert.Single(s.Preferences);Assert.Equal("contextual_choice",fact.Source);Assert.Equal("activity",fact.Scope);Assert.Equal("trip_only",fact.Status);
        Assert.Equal(question.Text,fact.Condition);Assert.Equal("on-route",fact.Choice!.OptionId);Assert.Equal(question.Id,fact.Choice.QuestionMessageId);
        Assert.Empty(TravelProfiling.Dna(s));Assert.False(TravelProfiling.PlanningFields(s).ContainsKey("interests"));
        Assert.Contains(question.Text,TravelProfiling.PlanningFields(s)["notes"]);
        s.Fields["interests"]="美食";
        Assert.Equal("美食",TravelProfiling.PlanningFields(s)["interests"]);
        Assert.Contains(question.Text,TravelProfiling.PlanningFields(s)["notes"]);
    }
    [Fact]
    public void StaleOrChangedOptionIsRejectedWithoutCountingARound()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        var question=s.Messages[0];var option=question.QuickReplies![0];var input=Selection(question,option);
        input["questionId"]="other-scene";Assert.Throws<ServiceException>(()=>KokoDialogue.ResolveChoice(s,input));
        input=Selection(question,option);input["message"]="替换后的不同内容";Assert.Throws<ServiceException>(()=>KokoDialogue.ResolveChoice(s,input));
        Assert.Equal(0,s.Dialogue!.ResponseCount);Assert.Empty(s.Preferences);
    }
    [Fact]
    public void UpgradingExistingDraftPreservesHistoryAndAlreadyUsedRounds()
    {
        var s=new PetSession{Fields=new(){["budget"]="200"}};
        for(var i=0;i<4;i++)s.Messages.Add(new("u"+i,"user","旧回答"));
        KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        Assert.Equal("200",s.Fields["budget"]);Assert.Equal(5,s.Messages.Count);Assert.Equal(4,s.Dialogue!.ResponseCount);
        Assert.NotNull(s.Dialogue.Inspiration);Assert.DoesNotContain("怎么称呼",s.Messages.Last().Text);
        KokoDialogue.Enable(s,new(){["profilingVersion"]=2});Assert.Equal(5,s.Messages.Count);
    }
    internal static JsonObject Selection(PetMessage message,PetQuickReply option)=>new(){["message"]=option.Text,["optionId"]=option.Id,["questionId"]=message.Id};
}

[Trait("Category","Integration")]
public sealed class KokoDialogueApiTests(ApiFixture fixture):IClassFixture<ApiFixture>
{
    private sealed class Model:IPetModel
    {
        public int Calls;
        public PetChoiceEvidence? SeenChoice;
        public Task<JsonObject> RespondAsync(PetSession session,string message,JsonArray memories,Func<string,Task> onReply,CancellationToken ct)
        {
            Calls++;SeenChoice=session.CurrentChoice;
            var text=Calls==1 ? "如果路线上有适合的展览，咱们顺路看看？":"那就把展览留成顺路的小停留，之后咱们坐下来吃点东西？";
            return Task.FromResult(new JsonObject{["reply"]=text,["ideas"]=new JsonArray(Calls==1 ? "如果路线上有适合的展览，可以顺路看看。":"顺路看看展览，再留一段坐下来吃东西的时间；场所待核实。"),
                ["quickReplies"]=new JsonArray(new JsonObject{["text"]="顺路再去"},new JsonObject{["text"]="这次想专程去看看"},new JsonObject{["text"]="换个想法吧",["intent"]="skip"}),
                ["preferences"]=new JsonArray(TravelProfilingUnitTests.Claim("interests","顺路看看展览",message)),
                ["fields"]=new JsonObject{["pace"]="5000",["date"]="2026-11-01"}});
        }
    }
    private static async Task<JsonObject> Create(HttpClient c)=>(await (await c.PostAsJsonAsync("pet/sessions",new{profilingVersion=2})).Content.ReadFromJsonAsync<JsonObject>())!;
    private static async Task<JsonObject> Turn(HttpClient c,string id,JsonObject input)
    {
        var response=await c.PostAsJsonAsync($"pet/sessions/{id}/turn",input);
        return JsonNode.Parse((await response.Content.ReadAsStringAsync()).Split('\n',StringSplitOptions.RemoveEmptyEntries).Last())!.AsObject();
    }
    private static JsonObject Click(JsonObject session,int index=0,string? requestId=null)
    {
        var question=session.Array("messages")!.Last()!;var option=question.Array("quickReplies")![index]!;
        return new(){["message"]=option.Str("text"),["questionId"]=question.Str("id"),["optionId"]=option.Str("id"),["requestId"]=requestId ?? Guid.NewGuid().ToString()};
    }
    [Fact]
    public async Task FourButtonResponsesEndInDraftWithBoundChoiceEvidenceAndNoInventedFacts()
    {
        var model=new Model();using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(model);}));
        using var client=factory.CreateClient();var s=await Create(client);var id=s.Str("id");
        for(var i=0;i<4;i++) {
            var evt=await Turn(client,id,Click(s));Assert.Equal("done",evt.Str("type"));s=evt.Object("session")!;
            Assert.Equal(i+1,s.Object("dialogue")!.Int("responseCount"));Assert.Equal(3,s.Array("messages")!.Last()!.Array("quickReplies")!.Count);
        }
        Assert.Equal(3,model.Calls);Assert.Equal("inspiration",s.Object("dialogue")!.Str("phase"));Assert.NotNull(model.SeenChoice);
        Assert.Empty(s.Object("fields")!);Assert.Empty(s.Array("travelDna")!);Assert.Empty(s.Array("pendingPreferences")!);
        Assert.Contains("场所待核实",s.Object("dialogue")!.Object("inspiration")!.Array("ideas")![0]!.GetValue<string>());
        var choice=Assert.Single(s.Array("messages")!.Where(x=>x!.Str("role")=="user" && x.Object("choice")?.Str("questionText").Contains("展览")==true).Take(1));
        Assert.Equal("顺路再去",choice!.Object("choice")!.Str("optionText"));
        Assert.All(s.Array("tripPreferences")!,p=>{Assert.Equal("activity",p!.Str("scope"));Assert.Equal("contextual_choice",p.Str("source"));Assert.NotNull(p.Object("choice"));});
    }
    [Fact]
    public async Task ClickRetryIsIdempotentButReusingOldButtonAsNewResponseIsRejected()
    {
        using var client=fixture.CreateFactory().CreateClient();var s=await Create(client);var id=s.Str("id");var click=Click(s);
        var first=await Turn(client,id,click);var retry=await Turn(client,id,click);
        Assert.Equal(first.ToJsonString(),retry.ToJsonString());
        click["requestId"]=Guid.NewGuid().ToString();var stale=await Turn(client,id,click);Assert.Equal("error",stale.Str("type"));
        var saved=(await client.GetFromJsonAsync<JsonObject>($"pet/sessions/{id}"))!;Assert.Equal(1,saved.Object("dialogue")!.Int("responseCount"));Assert.Equal(3,saved.Array("messages")!.Count);
    }
    [Fact]
    public async Task HurryProducesDraftWithoutNvidiaAndMemoryConsentDoesNotCountAsAnotherRound()
    {
        using var client=fixture.CreateFactory().CreateClient();var s=await Create(client);var id=s.Str("id");
        var evt=await Turn(client,id,new(){["message"]="别问了，直接安排",["requestId"]=Guid.NewGuid().ToString()});
        Assert.Equal("done",evt.Str("type"));s=evt.Object("session")!;Assert.NotNull(s.Object("dialogue")!.Object("inspiration"));
        Assert.Empty(s.Object("fields")!);Assert.False(s.Bool("dnaCompleted"));
        (await client.PostAsJsonAsync($"pet/sessions/{id}/memory",new{consent=false})).EnsureSuccessStatusCode();
        var saved=(await client.GetFromJsonAsync<JsonObject>($"pet/sessions/{id}"))!;
        Assert.Equal(1,saved.Object("dialogue")!.Int("responseCount"));Assert.Empty((await client.GetFromJsonAsync<JsonArray>("memories"))!);
    }
}
