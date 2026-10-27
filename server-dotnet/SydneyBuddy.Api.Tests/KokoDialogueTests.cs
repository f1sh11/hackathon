using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SydneyBuddy.Api.Tests;

public sealed class KokoDialogueUnitTests
{
    [Fact]
    public void PersonalizationPreservesNaturalFollowUpWithConfirmedBasics()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        s.PersonalizingTrip=true;s.Fields["destination"]="Sydney";s.Fields["date"]="2026-10-30";
        var reply=KokoDialogue.CompleteTurn(s,new(){["readyForDraft"]=true},"A quiet cafe sounds lovely. Do you enjoy waterfront walks too?","curious");
        Assert.Equal("A quiet cafe sounds lovely. Do you enjoy waterfront walks too?",reply.Text);
        Assert.Equal("2026-10-30",s.Fields["date"]);Assert.Empty(s.Preferences);
        Assert.DoesNotContain("trip details",reply.Text);
    }

    [Fact]
    public void AcceptingVisibleIdeaPreservesInterestsAndIsIdempotent() {
        var s=new PetSession{ResponseLanguage="en"};KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        s.Fields["interests"]="建筑、咖啡";s.Fields["date"]="2026-10-30";
        Assert.True(KokoDialogue.AcceptInspiration(s,"nature"));
        Assert.Equal("建筑、咖啡、自然、风景",s.Fields["interests"]);Assert.Equal("nature",s.Dialogue!.SelectedDirection);
        Assert.Equal("2026-10-30",s.Fields["date"]);Assert.Empty(s.Preferences);Assert.Empty(s.PendingMemory);
        Assert.Contains("Added to this trip",s.Messages.Last().Text);
        var count=s.Messages.Count;Assert.False(KokoDialogue.AcceptInspiration(s,"nature"));Assert.Equal(count,s.Messages.Count);
        var restored=JsonSerializer.Deserialize<PetSession>(JsonSerializer.Serialize(s))!;
        Assert.Equal("nature",restored.Dialogue!.SelectedDirection);Assert.False(KokoDialogue.AcceptInspiration(restored,"nature"));
        Assert.Throws<ServiceException>(()=>KokoDialogue.AcceptInspiration(s,"unsupported"));
    }
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
    public void AlternativeChangesIdeasWithoutChangingFactsAndAcceptanceIsTripOnly()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        var first=KokoDialogue.DirectResponse(s,"换个思路吧",null)!;
        KokoDialogue.CompleteTurn(s,first,first.Str("reply"),"curious");
        var ideas=s.Dialogue!.Ideas.ToArray();Assert.Empty(s.Fields);
        var second=KokoDialogue.DirectResponse(s,"换个思路吧",null)!;
        var reply=KokoDialogue.CompleteTurn(s,second,second.Str("reply"),"curious");
        Assert.NotEqual(ideas,s.Dialogue.Ideas);Assert.Empty(s.Fields);
        var accept=Assert.Single(reply.QuickReplies!,x=>x.Intent=="accept_direction");
        var accepted=KokoDialogue.DirectResponse(s,accept.Text,accept)!;
        var next=KokoDialogue.CompleteTurn(s,accepted,accepted.Str("reply"),"curious");
        Assert.Equal("文化、艺术",s.Fields["interests"]);Assert.Empty(s.Preferences);Assert.Empty(s.ConfirmedDna);
        Assert.Contains(next.QuickReplies!,x=>x.Intent=="choose_sydney");Assert.False(s.DnaCompleted);
        Assert.False(s.Dialogue.Inspiration!.PlacesVerified);
    }
    [Fact]
    public void BudgetQuestionClearsOnlyAmountAndOffersExplicitAmountsWithScope()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        PetQuestions.Apply(s,new(){["destination"]="悉尼",["date"]="2026-10-25",["days"]="2",["travelers"]="2",["budget"]="400",["budgetCurrency"]="AUD",["budgetBasis"]="groupTrip"});
        var result=KokoDialogue.DirectResponse(s,"预算还没想好，能先选个大概范围吗？",null)!;
        var reply=KokoDialogue.CompleteTurn(s,result,result.Str("reply"),"curious");
        Assert.False(s.Fields.ContainsKey("budget"));Assert.Equal("groupTrip",s.Fields["budgetBasis"]);Assert.Equal("2",s.Fields["travelers"]);
        Assert.Contains("原预算先不采用",reply.Text);Assert.Contains(reply.QuickReplies!,x=>x.Intent=="budget_120");
        var option=reply.QuickReplies!.Single(x=>x.Intent=="budget_120");
        result=KokoDialogue.DirectResponse(s,option.Text,option)!;
        KokoDialogue.CompleteTurn(s,result,result.Str("reply"),"curious");
        Assert.Equal("120",s.Fields["budget"]);Assert.Equal("perPersonPerDay",s.Fields["budgetBasis"]);Assert.Equal("AUD",s.Fields["budgetCurrency"]);
        Assert.Equal("",KokoDialogue.NextNeed(s));Assert.Empty(s.Preferences);
    }
    [Fact]
    public void FreeTextTripFactsAdvanceImmediatelyWithoutFourInterviewTurns()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});s.Fields["destination"]="悉尼";
        var reply=KokoDialogue.CompleteTurn(s,new(),"还想选哪种玩法？","curious");
        Assert.NotNull(s.Dialogue!.Inspiration);Assert.Contains(reply.QuickReplies!,x=>x.Intent=="details_time");
        Assert.Contains("出发日期",reply.Text);Assert.DoesNotContain("哪种玩法",reply.Text);Assert.False(s.Fields.ContainsKey("date"));
    }
    [Fact]
    public void DeferredDateKeepsUnknownAndMovesOnToPeopleAndBudget()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});s.Fields["destination"]="悉尼";
        var result=KokoDialogue.DirectResponse(s,"日期还没定",new PetQuickReply("defer","日期还没定","defer_time"))!;
        var reply=KokoDialogue.CompleteTurn(s,result,result.Str("reply"),"curious");
        Assert.False(s.Fields.ContainsKey("date"));Assert.Contains(reply.QuickReplies!,x=>x.Intent=="people_1");
        s.Fields["travelers"]="1";Assert.Equal("budget",KokoDialogue.NextNeed(s));
        s.Fields["budget"]="120";s.Fields["budgetCurrency"]="AUD";s.Fields["budgetBasis"]="perPersonPerDay";
        Assert.Equal("time",KokoDialogue.NextNeed(s));
        KokoDialogue.DetailsUpdated(s,new(){["budget"]="120"});
        Assert.True(s.Dialogue!.DateDeferred);
        Assert.DoesNotContain(s.Messages.Last().QuickReplies!,x=>x.Intent=="defer_time");
        Assert.Contains(s.Messages.Last().QuickReplies!,x=>x.Intent=="details_time");
        Assert.False(PetQuestions.Answered(s,"date"));
    }
    [Fact]
    public void UnsupportedCityAndPartialUpdatesKeepAnActionableNextStep()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        PetQuestions.Apply(s,new(){["destination"]="墨尔本",["date"]="2026-10-25",["days"]="2"});
        KokoDialogue.DetailsUpdated(s,new(){["destination"]="墨尔本"});
        Assert.Contains("只支持悉尼",s.Messages.Last().Text);Assert.Contains(s.Messages.Last().QuickReplies!,x=>x.Intent=="keep_draft");
        var choice=s.Messages.Last().QuickReplies!.Single(x=>x.Intent=="choose_sydney");
        var result=KokoDialogue.DirectResponse(s,choice.Text,choice)!;
        var reply=KokoDialogue.CompleteTurn(s,result,result.Str("reply"),"curious");
        Assert.Equal("2026-10-25",s.Fields["date"]);Assert.Equal("2",s.Fields["days"]);
        Assert.Contains(reply.QuickReplies!,x=>x.Intent=="people_2");
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
    public void PlainNameAnswersAreRememberedButAnInterestStatementIsNotMisreadAsAName()
    {
        var s=new PetSession();KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        Assert.NotNull(KokoDialogue.DirectResponse(s,"Sam",null));Assert.Equal("Sam",s.Dialogue!.Nickname);
        var other=new PetSession();KokoDialogue.Enable(other,new(){["profilingVersion"]=2});
        Assert.Null(KokoDialogue.DirectResponse(other,"我喜欢动漫，平时宅家",null));Assert.Empty(other.Dialogue!.Nickname);
        Assert.Null(KokoDialogue.DirectResponse(other,"hello",null));Assert.Empty(other.Dialogue.Nickname);
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
    [Fact]
    public void UpgradingAnEmptyLegacyDraftReplacesItsWelcomeInsteadOfIntroducingTwice()
    {
        var s=new PetSession{Messages=[new("old-welcome","assistant","嗨，我是 Buddy")],Fields=new(){["budget"]="80"}};
        KokoDialogue.Enable(s,new(){["profilingVersion"]=2});
        var hello=Assert.Single(s.Messages);Assert.Contains("Koko",hello.Text);Assert.DoesNotContain("Buddy",hello.Text);
        Assert.Equal(3,hello.QuickReplies!.Length);Assert.Equal("80",s.Fields["budget"]);Assert.Equal(0,s.Dialogue!.ResponseCount);
    }
    [Fact]
    public void LegacyDeferredDateSurvivesBudgetUpdateAndOldChoiceReplayIsRecognized() {
        var s=new PetSession{Dialogue=new(){RequestedField="time"},ResponseLanguage="en"};
        s.Fields["destination"]="悉尼";
        s.Messages.Add(new("old-choice","user","Date still open",Choice:new("old-question","Date?","later","日期还没定")));
        KokoDialogue.DetailsUpdated(s,new(){["budget"]="120"});
        KokoDialogue.RefreshDeferredState(s);
        Assert.True(s.Dialogue.DateDeferred);Assert.Equal("travelers",KokoDialogue.NextNeed(s));
        var count=s.Messages.Count;
        Assert.True(KokoDialogue.RepeatedChoice(s,new(){["questionId"]="old-question",["optionId"]="later",["message"]="Date still open"}));
        Assert.False(KokoDialogue.RepeatedChoice(s,new(){["questionId"]="old-question",["optionId"]="later",["message"]="Different answer"}));
        Assert.Equal(count,s.Messages.Count);Assert.Empty(s.Preferences);Assert.False(s.Fields.ContainsKey("date"));
        s.Fields["date"]="2026-11-01";s.Fields["days"]="2";KokoDialogue.RefreshDeferredState(s);
        Assert.False(s.Dialogue.DateDeferred);
    }
    [Fact]
    public void ExplicitNoRequirementsIsAnsweredButUnknownIsNot() {
        var s=new PetSession();
        PetQuestions.Apply(s,new(){["avoid"]="无特别要求"});
        Assert.True(PetQuestions.Answered(s,"avoid"));
        Assert.False(PetQuestions.Known("暂不确定"));
        Assert.True(PetQuestions.Known("无特别要求"));
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
                ["fields"]=session.CurrentChoice is null ? new JsonObject():new JsonObject{["pace"]="5000",["date"]="2026-11-01"}});
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
    public async Task FourMixedResponsesEndInDraftWithBoundChoiceEvidenceAndNoInventedFacts()
    {
        var model=new Model();using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(model);}));
        using var client=factory.CreateClient();var s=await Create(client);var id=s.Str("id");
        for(var i=0;i<4;i++) {
            var input=i==1 ? new JsonObject{["message"]="这次我想看看展览",["requestId"]=Guid.NewGuid().ToString()}:Click(s);
            var evt=await Turn(client,id,input);Assert.Equal("done",evt.Str("type"));s=evt.Object("session")!;
            Assert.Equal(i+1,s.Object("dialogue")!.Int("responseCount"));Assert.Equal(3,s.Array("messages")!.Last()!.Array("quickReplies")!.Count);
        }
        Assert.Equal(3,model.Calls);Assert.Equal("inspiration",s.Object("dialogue")!.Str("phase"));Assert.NotNull(model.SeenChoice);
        Assert.Empty(s.Object("fields")!);Assert.Empty(s.Array("travelDna")!);Assert.Empty(s.Array("pendingPreferences")!);
        Assert.Contains("场所待核实",s.Object("dialogue")!.Object("inspiration")!.Array("ideas")![0]!.GetValue<string>());
        var choice=Assert.Single(s.Array("messages")!.Where(x=>x!.Str("role")=="user" && x.Object("choice")?.Str("questionText").Contains("展览")==true).Take(1));
        Assert.Equal("顺路再去",choice!.Object("choice")!.Str("optionText"));
        Assert.All(s.Array("tripPreferences")!.Where(p=>p!.Str("source")=="contextual_choice"),p=>{Assert.Equal("activity",p!.Str("scope"));Assert.Equal("contextual_choice",p.Str("source"));Assert.NotNull(p.Object("choice"));});
    }
    [Fact]
    public async Task SavedDetailRefreshesQuestionAndButtonsWithoutRepeatingKnownFacts()
    {
        var model=new Model();using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(model);}));
        using var client=factory.CreateClient();var s=await Create(client);var id=s.Str("id");
        s=(await Turn(client,id,Click(s))).Object("session")!;
        s=(await Turn(client,id,Click(s))).Object("session")!; // accepted nature
        s=(await Turn(client,id,Click(s))).Object("session")!; // Sydney
        Assert.Equal("悉尼",s.Object("fields")!.Str("destination"));
        var patch=await client.PatchAsJsonAsync($"pet/sessions/{id}",new{fields=new{date="2026-10-25",days="2"}});
        patch.EnsureSuccessStatusCode();s=(await patch.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Contains("几个人",s.Array("messages")!.Last()!.Str("text"));
        s=(await Turn(client,id,Click(s,1))).Object("session")!; // two people
        Assert.Equal("2",s.Object("fields")!.Str("travelers"));
        s=(await Turn(client,id,Click(s,1))).Object("session")!; // 120 AUD/person/day
        Assert.Equal("120",s.Object("fields")!.Str("budget"));Assert.Equal("perPersonPerDay",s.Object("fields")!.Str("budgetBasis"));
        Assert.Equal(0,model.Calls);Assert.Empty(s.Array("travelDna")!);
        s=(await Turn(client,id,new(){["message"]="预算还没想好，能先选个大概范围吗？",["requestId"]=Guid.NewGuid().ToString()})).Object("session")!;
        Assert.False(s.Object("fields")!.ContainsKey("budget"));Assert.Contains("原预算先不采用",s.Array("messages")!.Last()!.Str("text"));
        s=(await Turn(client,id,Click(s))).Object("session")!;
        Assert.Equal("60",s.Object("fields")!.Str("budget"));Assert.Equal("2026-10-25",s.Object("fields")!.Str("date"));Assert.Equal(0,model.Calls);
    }
    [Fact]
    public async Task ClickRetryAndRepeatedHandledChoiceAreNoOpsButTamperingIsRejected()
    {
        using var client=fixture.CreateFactory().CreateClient();var s=await Create(client);var id=s.Str("id");var click=Click(s);
        var first=await Turn(client,id,click);var retry=await Turn(client,id,click);
        Assert.Equal(first.ToJsonString(),retry.ToJsonString());
        click["requestId"]=Guid.NewGuid().ToString();var stale=await Turn(client,id,click);Assert.Equal("done",stale.Str("type"));
        click["requestId"]=Guid.NewGuid().ToString();click["message"]="changed message";Assert.Equal("error",(await Turn(client,id,click)).Str("type"));
        var saved=(await client.GetFromJsonAsync<JsonObject>($"pet/sessions/{id}"))!;Assert.Equal(1,saved.Object("dialogue")!.Int("responseCount"));Assert.Equal(3,saved.Array("messages")!.Count);
    }
    [Fact]
    public async Task DeferredDateSurvivesReloadAndBudgetEditWithoutOfferingAnotherDeferral() {
        using var client=fixture.CreateFactory().CreateClient();var s=await Create(client);var id=s.Str("id");
        var response=await client.PatchAsJsonAsync($"pet/sessions/{id}",new{fields=new Dictionary<string,string>{{"destination","悉尼"},{"travelers","1"},{"budget","60"},{"budgetCurrency","AUD"},{"budgetBasis","perPersonPerDay"}}});
        s=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var options=s.Array("messages")!.Last()!.Array("quickReplies")!;
        var index=options.Select((x,i)=>(x,i)).Single(x=>x.x!.Str("intent")=="defer_time").i;
        s=(await Turn(client,id,Click(s,index))).Object("session")!;
        Assert.True(s.Object("dialogue")!.Bool("dateDeferred"));Assert.False(s.Object("fields")!.ContainsKey("date"));
        Assert.DoesNotContain(s.Array("messages")!.Last()!.Array("quickReplies")!,x=>x!.Str("intent")=="defer_time");
        s=(await client.GetFromJsonAsync<JsonObject>($"pet/sessions/{id}"))!;
        Assert.True(s.Object("dialogue")!.Bool("dateDeferred"));
        response=await client.PatchAsJsonAsync($"pet/sessions/{id}",new{fields=new{budget="120"}});
        s=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(s.Object("dialogue")!.Bool("dateDeferred"));Assert.DoesNotContain(s.Array("messages")!.Last()!.Array("quickReplies")!,x=>x!.Str("intent")=="defer_time");
        response=await client.PatchAsJsonAsync($"pet/sessions/{id}",new{fields=new{date="2026-11-01",days="2"}});
        s=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.False(s.Object("dialogue")!.Bool("dateDeferred"));Assert.Equal("2026-11-01",s.Object("fields")!.Str("date"));
        Assert.Empty(s.Array("travelDna")!);
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
