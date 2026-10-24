using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace SydneyBuddy.Api.Tests;

[Trait("Category","Integration")]
public sealed class PetConversationTests(ApiFixture fixture):IClassFixture<ApiFixture>
{
    [Fact]
    public void TravelFactsCannotSmuggleUnstatedHabitsIntoKokoTripFields()
    {
        var fields=new JsonObject{["energy"]="中等精力",["rhythm"]="紧凑充实",["queueTolerance"]="不排队",["pace"]="5000",["destination"]="悉尼"};
        PetExplicitFacts.FilterUnsupportedHabits("这次去悉尼，明天玩2天。",fields);
        Assert.Single(fields);Assert.Equal("悉尼",fields.Str("destination"));
        fields=new JsonObject{["pace"]="10000",["interests"]="自然"};
        PetExplicitFacts.FilterUnsupportedHabits("这次步数10000以内，想看自然风景。",fields);
        Assert.Equal(2,fields.Count);
    }
    [Fact]
    public void CompleteTripSentenceRetainsRequiredFactsWithoutModelExtraction()
    {
        var fields=new JsonObject();PetExplicitFacts.Apply("这次去悉尼，明天开始玩2天，一个人，每人每天活动预算100澳元，从环形码头出发，这次步数10000以内，必须去悉尼歌剧院，不想去博物馆。",fields);
        Assert.Equal("悉尼",fields.Str("destination"));Assert.Equal("2",fields.Str("days"));
        Assert.Equal("perPersonPerDay",fields.Str("budgetBasis"));Assert.Equal("activities",fields.Str("budgetCoverage"));
        Assert.Equal("约10000步",fields.Str("pace"));Assert.False(string.IsNullOrEmpty(fields.Str("date")));
    }
    [Fact]
    public void AmbiguousRelativeDatesAndDurationsAreNotGuessed()
    {
        var fields=new JsonObject();PetExplicitFacts.Apply("今天或明天出发，玩2天或3天，还没确定。",fields);
        Assert.Null(fields["date"]);Assert.Null(fields["days"]);
    }

    [Fact]
    public void ExplicitMustAndAvoidAreKeptEvenWhenModelOnlyExtractsBudget() {
        var fields=new JsonObject();PetExplicitFacts.Apply("两个人，每人每天100澳元活动预算，必须去悉尼歌剧院，不想逛博物馆。只影响这次旅行。",fields);
        Assert.Equal("悉尼歌剧院",fields.Str("mustDo"));Assert.Equal("博物馆",fields.Str("avoid"));Assert.Equal("100",fields.Str("budget"));
        var negated=new JsonObject();PetExplicitFacts.Apply("不是必须去悉尼歌剧院，不需要避开博物馆。",negated);
        Assert.Null(negated["mustDo"]);Assert.Null(negated["avoid"]);
    }
    [Fact]
    public void ExplicitGroupBudgetOverridesEmptyOrIncorrectModelAmount()
    {
        var fields=new JsonObject{["budget"]="",["budgetBasis"]="perPersonTrip"};
        PetExplicitFacts.Apply("2个人，两人全程活动预算400澳元，每天最多8000步",fields);
        Assert.Equal("400",fields.Str("budget"));Assert.Equal("澳元",fields.Str("budgetCurrency"));
        Assert.Equal("groupTrip",fields.Str("budgetBasis"));Assert.Equal("约8000步",fields.Str("pace"));
    }
    [Theory]
    [InlineData("预算1000或2000澳元")]
    [InlineData("不是400澳元")]
    public void AmbiguousOrNegatedAmountIsNotAnExplicitBudget(string message)
    {
        var fields=new JsonObject();PetExplicitFacts.Apply(message,fields);Assert.Null(fields["budget"]);
    }
    [Theory]
    [InlineData("每天最多1000步", "约1000步")]
    [InlineData("这次步数10000以内", "约10000步")]
    [InlineData("步數：8,000以內，预算100元", "约8000步")]
    [InlineData("每天最多 10,000 步", "约10000步")]
    public void ExplicitStepLimitOverridesQualitativeModelSummary(string message,string expected)
    {
        var fields=new JsonObject{["pace"]="轻松"};PetExplicitFacts.Apply(message,fields);
        Assert.Equal(expected,fields.Str("pace"));
    }
    [Fact]
    public void StepRangeIsNotSilentlyConvertedToAnExactLimit()
    {
        var fields=new JsonObject();PetExplicitFacts.Apply("每天3000-6000步",fields);
        Assert.Null(fields["pace"]);
    }
    [Theory]
    [InlineData("relaxed","relaxed")]
    [InlineData("excited","excited")]
    [InlineData("angry","curious")]
    [InlineData("","curious")]
    public void ReplyEmotionOnlyAcceptsSafeKnownStates(string input,string expected)=>Assert.Equal(expected,PetEmotion.Normalize(input));
    [Fact]
    public void UnknownRequiredAnswersDoNotAdvanceButOptionalUnknownsMayBeSkipped()
    {
        var session=new PetSession();
        foreach(var key in PetQuestions.Required)session.Fields[key]="暂不确定";
        Assert.Equal("destination",PetQuestions.Next(session));
        Assert.Equal(0,((JsonObject)JsonSerializerNode(PetQuestions.View(session))).Int("completed"));
        PetQuestions.Apply(session,new JsonObject{["destination"]="悉尼",["date"]="2026-11-01",["days"]="2",["travelers"]="2",["budget"]="100",["budgetBasis"]="perPersonPerDay",["budgetCurrency"]="AUD"});
        session.Fields["destination"]="悉尼";session.Fields["budgetCurrency"]="AUD";session.Fields["origin"]="稍后补充";
        Assert.Equal("",PetQuestions.Next(session));
    }
    private static JsonNode JsonSerializerNode(object value)=>System.Text.Json.JsonSerializer.SerializeToNode(value,JsonExtensions.Options)!;
    [Theory]
    [InlineData("date","明年夏天")]
    [InlineData("days","16")]
    [InlineData("budget","NaN")]
    [InlineData("budgetCurrency","CNY")]
    [InlineData("budgetBasis","差不多")]
    [InlineData("travelers","0")]
    public void InvalidCoreValuesNeverCompletePreparation(string field,string value) {
        var session=new PetSession();
        PetQuestions.Apply(session,new JsonObject{["destination"]="悉尼",["date"]="2026-11-01",["days"]="2",["travelers"]="2",["budget"]="100",["budgetBasis"]="perPersonPerDay",["budgetCurrency"]="AUD"});
        session.Fields[field]=value;Assert.Equal(field,PetQuestions.Next(session));Assert.False(PetQuestions.Answered(session,field));
    }
    [Fact]
    public async Task AmbiguousCorrectionClearsOldValueInsteadOfLeavingGenerationReady() {
        var fake=new PetFake{Response=JsonNode.Parse("""{"reply":"下周哪天出发呀？","fields":{},"needsClarification":["date"],"memorySuggestion":""}""")!.AsObject()};
        using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(fake);}));using var client=factory.CreateClient(new(){BaseAddress=new Uri("http://localhost")});var id=(await Create(client)).Str("id");
        await client.PatchAsJsonAsync($"/pet/sessions/{id}",new{fields=new{destination="悉尼",date="2026-11-01",days="2",budget="100",budgetCurrency="AUD",budgetBasis="perPersonPerDay",travelers="2"}});
        var result=await Turn(client,id,new{message="改成下周吧",requestId=Guid.NewGuid().ToString()});Assert.Equal("date",result.Str("nextField"));Assert.False(result.Object("fields")!.ContainsKey("date"));
    }
    [Fact]
    public async Task TemporaryPreferenceNeverCreatesLongTermSuggestionEvenIfModelSuggestsIt() {
        var fake=new PetFake{Response=JsonNode.Parse("""{"reply":"收到","fields":{"rhythm":"轻松随意"},"memorySuggestion":"喜欢轻松旅行"}""")!.AsObject()};
        using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(fake);}));
        using var client=factory.CreateClient(new(){BaseAddress=new Uri("http://localhost")});var id=(await Create(client)).Str("id");
        var result=await Turn(client,id,new{message="这次想轻松一点",requestId=Guid.NewGuid().ToString()});Assert.Equal("",result.Str("pendingMemory"));
    }
    [Fact]
    public async Task UnsupportedCityCannotClaimReadyAndCanBeRepairedWithoutLosingRequirements()
    {
        using var client=fixture.CreateFactory().CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var id=(await Create(client)).Str("id");
        await client.PatchAsJsonAsync($"/pet/sessions/{id}",new{fields=new{destination="墨尔本",date="2026-11-01",days="2",budget="100",budgetCurrency="AUD",budgetBasis="perPersonPerDay",travelers="2",origin="稍后补充",companions="两个人",mustDo="无"}});
        var blocked=await Turn(client,id,new{message="无特别要求",field="avoid",value="无特别要求",requestId=Guid.NewGuid().ToString()});
        Assert.Equal("destination",blocked.Str("nextField"));Assert.Equal(6,blocked.Int("completed"));
        Assert.DoesNotContain("准备好",blocked.Array("messages")!.Last()!.Str("text"));
        var repaired=await Turn(client,id,new{message="改为悉尼",field="destination",value="悉尼",requestId=Guid.NewGuid().ToString()});
        Assert.Equal("",repaired.Str("nextField"));Assert.Equal("2",repaired.Object("fields")!.Str("days"));
        Assert.Contains("生成行程",repaired.Array("messages")!.Last()!.Str("text"));
    }
    [Fact]
    public async Task CurrencyRepairClearsTheOldAmountAndDoesNotClaimReadiness()
    {
        using var client=fixture.CreateFactory().CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var session=await Create(client);
        var id=session.Str("id");
        await client.PatchAsJsonAsync($"/pet/sessions/{id}",new{fields=new{destination="悉尼",date="2026-11-01",days="2",budget="100",budgetCurrency="CNY",budgetBasis="groupTrip",travelers="2",origin="未确定",companions="两个人",mustDo="无",avoid="无"}});
        var current=await client.GetFromJsonAsync<JsonObject>($"/pet/sessions/{id}");
        Assert.Equal("budgetCurrency",current!.Str("nextField"));
        Assert.Equal(6,current.Int("completed"));
        var response=await client.PostAsJsonAsync($"/pet/sessions/{id}/turn",new{message="AUD",field="budgetCurrency",value="AUD",requestId=Guid.NewGuid().ToString()});
        var last=JsonNode.Parse((await response.Content.ReadAsStringAsync()).Split('\n',StringSplitOptions.RemoveEmptyEntries).Last())!.Object("session")!;
        Assert.Equal("budget",last.Str("nextField"));Assert.False(last.Object("fields")!.ContainsKey("budget"));
        Assert.DoesNotContain("准备好",last.Array("messages")!.Last()!.Str("text"));
    }
    [Fact]
    public void ExplicitNoMustDoOrAvoidCountsAsAnswered()
    {
        var fields=new JsonObject();
        PetExplicitFacts.Apply("这次没有必去地点，也没有特别需要避开的安排。",fields);
        Assert.Equal("无",fields.Str("mustDo"));Assert.Equal("无",fields.Str("avoid"));
    }
    [Theory]
    [InlineData("destination","还没想好")]
    [InlineData("date","还没确定")]
    [InlineData("days","未确定")]
    [InlineData("budget","暂不确定")]
    [InlineData("budgetCurrency","未知")]
    [InlineData("budgetBasis","未确定")]
    [InlineData("travelers","不知道")]
    public async Task UncertainRequiredChoicePoutsAndStaysOnQuestion(string field,string value)
    {
        using var client=fixture.CreateFactory().CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var id=(await Create(client)).Str("id");
        var response=await client.PostAsJsonAsync($"/pet/sessions/{id}/turn",new{message=value,field,value,requestId=Guid.NewGuid().ToString()});
        var lines=(await response.Content.ReadAsStringAsync()).Split('\n',StringSplitOptions.RemoveEmptyEntries);
        var session=JsonNode.Parse(lines.Last())!.Object("session")!;
        Assert.Equal(0,session.Int("completed"));
        Assert.Equal("pouting",session.Array("messages")!.Last()!.Str("mood"));
        Assert.Equal(PetQuestions.UncertainReply(field),JsonNode.Parse(lines.First())!.Str("text"));
    }
    [Fact]
    public async Task IncorrectModelBudgetIsNeverStreamedOrSavedAsTheFinalReply()
    {
        var fake=new PetFake{StreamedReply="每人每天80澳元",Response=JsonNode.Parse("""{"reply":"每人每天80澳元","fields":{"days":"5","budget":"2000","budgetCurrency":"AUD","budgetBasis":"groupTrip","travelers":"2"},"memorySuggestion":""}""")!.AsObject()};
        using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(fake);}));
        using var client=factory.CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var id=(await Create(client)).Str("id");
        var response=await client.PostAsJsonAsync($"/pet/sessions/{id}/turn",new{message="两个人玩5天，全程活动预算一共2000澳元",requestId=Guid.NewGuid().ToString()});
        var body=await response.Content.ReadAsStringAsync();
        var events=body.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(x=>JsonNode.Parse(x)!.AsObject()).ToArray();
        Assert.DoesNotContain(events.Where(e=>e.Str("type")=="reply"),e=>e.Str("text").Contains("80"));
        Assert.Contains("200 AUD",events.Last().Object("session")!.Array("messages")!.Last()!.Str("text"));
    }
    [Fact]
    public void ExplicitFallbackDoesNotOverwriteCorrectionsOrNegatedPreferences()
    {
        var fields=new JsonObject{["travelers"]="3",["origin"]="中央车站",["rhythm"]="紧凑充实"};
        PetExplicitFacts.Apply("不是两个人，是三个人；从环形码头改为中央车站出发。不喜欢文化，这次不要轻松。",fields);
        Assert.Equal("3",fields.Str("travelers"));Assert.Equal("中央车站",fields.Str("origin"));
        Assert.Equal("紧凑充实",fields.Str("rhythm"));Assert.False(fields.ContainsKey("interests"));
    }
    [Fact]
    public void ExplicitFactsAndBudgetMathAreIndependentOfModelCalculations()
    {
        var changes=new JsonObject{["days"]="5"};
        PetExplicitFacts.Apply("两个人，全程活动预算一共2000澳元，每天从中央车站出发，这次轻松，喜欢文化。",changes);
        var session=new PetSession();PetQuestions.Apply(session,changes);
        Assert.Equal("2",session.Fields["travelers"]);Assert.Equal("中央车站",session.Fields["origin"]);
        Assert.Equal("文化",session.Fields["interests"]);Assert.Equal("groupTrip",session.Fields["budgetBasis"]);
        Assert.Contains("200 AUD",PetExplicitFacts.BudgetReply(session));
    }
    [Fact]
    public void HumanCurrencyAndExplicitCompanionCountNormalizeWithoutInventingHabits()
    {
        var session=new PetSession();
        PetQuestions.Apply(session,new JsonObject{["companions"]="两个人",["budgetCurrency"]="澳元",["budgetCoverage"]="活动预算",["rhythm"]="文化"});
        Assert.Equal("2",session.Fields["travelers"]);Assert.Equal("AUD",session.Fields["budgetCurrency"]);
        Assert.Equal("activities",session.Fields["budgetCoverage"]);Assert.False(session.Fields.ContainsKey("rhythm"));
    }
    [Fact]
    public async Task TemporaryTripRhythmDoesNotChangePersonalSnapshotOrCreateMemory()
    {
        var fake=new PetFake{Response=JsonNode.Parse("""{"reply":"这次我们慢慢逛。","fields":{"rhythm":"轻松随意"},"memorySuggestion":""}""")!.AsObject()};
        using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(fake);}));
        using var client=factory.CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var created=await client.PostAsJsonAsync("/pet/sessions",new{personalProfile=new{rhythm="紧凑充实",dailySteps=10000}});
        var session=(await created.Content.ReadFromJsonAsync<JsonObject>())!;
        var result=await Turn(client,session.Str("id"),new{message="这次想轻松一点",requestId=Guid.NewGuid().ToString()});
        Assert.Equal("轻松随意",result.Object("fields").Str("rhythm"));
        Assert.Equal("紧凑充实",fake.LastProfile.Str("rhythm"));
        Assert.Empty(result.Str("pendingMemory"));
    }
    [Fact]
    public void TravelRequirementsRetainBudgetScopeAndSkipAlreadyProvidedInformation()
    {
        var session=new PetSession();
        PetQuestions.Apply(session,new JsonObject{["destination"]="悉尼",["date"]="2026-11-01",["days"]="5",["budget"]="2000",["budgetCurrency"]="AUD",["budgetBasis"]="groupTrip",["travelers"]="2",["origin"]="稍后补充",["companions"]="朋友",["mustDo"]="艺术",["avoid"]="爬坡"});
        Assert.Equal("",PetQuestions.Next(session));
        Assert.Equal("groupTrip",session.Fields["budgetBasis"]);
        Assert.Equal("爬坡",session.Fields["avoid"]);
        Assert.Empty(session.PersonalProfile);
    }
    [Fact]
    public async Task OptionsAndChatShareStateSurviveRestartAndNeverLeakTripIntoNewSession()
    {
        var fake=new PetFake();
        using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(fake);}));
        using var client=factory.CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var session=await Create(client);var id=session.Str("id");
        var turn=new {message="墨尔本",field="destination",value="墨尔本",requestId=Guid.NewGuid().ToString()};
        var fixedResult=await Turn(client,id,turn);Assert.Equal("墨尔本",fixedResult.Object("fields").Str("destination"));
        var before=fixedResult.Array("messages")!.Count;
        var duplicate=await Turn(client,id,turn);Assert.Equal(before,duplicate.Array("messages")!.Count);
        fake.Response=JsonNode.Parse("""{"reply":"艺术和慢节奏，收到啦。想玩几天？","fields":{"interests":"艺术","pace":"慢悠悠"},"memorySuggestion":"喜欢艺术和慢节奏","mood":"relaxed"}""")!.AsObject();
        var chatResult=await Turn(client,id,new {message="我平时一直喜欢艺术和慢节奏",requestId=Guid.NewGuid().ToString()});
        Assert.Equal("墨尔本",chatResult.Object("fields").Str("destination"));
        Assert.Equal("艺术",chatResult.Object("fields").Str("interests"));
        Assert.Equal("喜欢艺术和慢节奏",chatResult.Str("pendingMemory"));
        Assert.Equal("relaxed",chatResult.Array("messages")!.Last()!.Str("mood"));
        Assert.Empty((await client.GetFromJsonAsync<JsonArray>("/memories"))!);
        var local=await client.PostAsJsonAsync($"/pet/sessions/{id}/memory",new {consent=false});local.EnsureSuccessStatusCode();
        Assert.Empty((await client.GetFromJsonAsync<JsonArray>("/memories"))!);
        using var restarted=fixture.CreateFactory();using var other=restarted.CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var restored=await other.GetFromJsonAsync<JsonObject>($"/pet/sessions/{id}");
        Assert.Equal("墨尔本",restored!.Object("fields").Str("destination"));
        Assert.Contains("艺术",restored.Object("fields").Str("notes"));
        var fresh=await Create(other);Assert.Empty(fresh.Object("fields")!);Assert.NotEqual(id,fresh.Str("id"));
        await other.DeleteAsync($"/pet/sessions/{id}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await other.GetAsync($"/pet/sessions/{id}")).StatusCode);
    }
    [Fact]
    public async Task ConsentEditAndRevocationControlWhatSubsequentChatReceives()
    {
        var fake=new PetFake{Response=JsonNode.Parse("""{"reply":"要记住吗？","fields":{},"memorySuggestion":"喜欢安静的餐厅"}""")!.AsObject()};
        using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(fake);}));
        using var client=factory.CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var id=(await Create(client)).Str("id");
        await Turn(client,id,new{message="我平时一直喜欢安静的餐厅",requestId=Guid.NewGuid().ToString()});
        (await client.PostAsJsonAsync($"/pet/sessions/{id}/memory",new{consent=true})).EnsureSuccessStatusCode();
        var records=(await client.GetFromJsonAsync<JsonArray>("/memories"))!;var memory=records.First(x=>x.Str("summary")=="喜欢安静的餐厅")!;
        fake.Response=JsonNode.Parse("""{"reply":"一起慢慢玩","fields":{},"memorySuggestion":""}""")!.AsObject();
        await Turn(client,id,new{message="给我一些建议",requestId=Guid.NewGuid().ToString()});Assert.Contains("喜欢安静的餐厅",fake.LastMemories.ToJsonString(JsonExtensions.Options));
        var edited=await client.PutAsJsonAsync("/memories/"+memory.Str("id"),new{summary="喜欢热闹的小馆子",consent=true});edited.EnsureSuccessStatusCode();
        var updated=(await edited.Content.ReadFromJsonAsync<JsonObject>())!;
        await Turn(client,id,new{message="再推荐一下",requestId=Guid.NewGuid().ToString()});
        Assert.DoesNotContain("喜欢安静的餐厅",fake.LastMemories.ToJsonString(JsonExtensions.Options));Assert.Contains("热闹",fake.LastMemories.ToJsonString(JsonExtensions.Options));
        (await client.DeleteAsync("/memories/"+updated.Str("id"))).EnsureSuccessStatusCode();
        await Turn(client,id,new{message="再推荐一下",requestId=Guid.NewGuid().ToString()});Assert.DoesNotContain("热闹",fake.LastMemories.ToJsonString(JsonExtensions.Options));
    }
    [Fact]
    public async Task FailedStreamDoesNotCommitPartialFieldsOrDuplicateMessages()
    {
        var fake=new PetFake{Fail=true};
        using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(fake);}));
        using var client=factory.CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var id=(await Create(client)).Str("id");var body=new{message="明天去悉尼",requestId=Guid.NewGuid().ToString()};
        var response=await client.PostAsJsonAsync($"/pet/sessions/{id}/turn",body);var data=await response.Content.ReadAsStringAsync();Assert.Contains("error",data);
        var restored=(await client.GetFromJsonAsync<JsonObject>($"/pet/sessions/{id}"))!;Assert.Single(restored.Array("messages")!);Assert.Empty(restored.Object("fields")!);
        fake.Fail=false;var result=await Turn(client,id,body);Assert.Equal(3,result.Array("messages")!.Count);
        var invalid=await client.PostAsJsonAsync($"/pet/sessions/{id}/turn",new{message="hi",requestId="invalid"});Assert.Equal(HttpStatusCode.UnprocessableEntity,invalid.StatusCode);
    }
    private static async Task<JsonObject> Create(HttpClient client)=>(await (await client.PostAsJsonAsync("/pet/sessions",new {})).Content.ReadFromJsonAsync<JsonObject>())!;
    private static async Task<JsonObject> Turn(HttpClient client,string id,object body)
    {
        var response=await client.PostAsJsonAsync($"/pet/sessions/{id}/turn",body);response.EnsureSuccessStatusCode();
        var lines=(await response.Content.ReadAsStringAsync()).Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(x=>JsonNode.Parse(x)!.AsObject()).ToArray();
        Assert.Contains(lines,x=>x.Str("type")=="done");
        return lines.Last().Object("session")!;
    }
    private sealed class PetFake:IPetModel
    {
        public JsonObject Response {get;set;}=new(){["reply"]="收到啦",["fields"]=new JsonObject(),["memorySuggestion"]=""};
        public bool Fail {get;set;}
        public string StreamedReply {get;set;}="收到";
        public JsonArray LastMemories {get;private set;}=[];
        public JsonObject LastProfile {get;private set;}=new();
        public async Task<JsonObject> RespondAsync(PetSession session,string message,JsonArray memories,Func<string,Task> onReply,CancellationToken ct)
        {LastProfile=(JsonObject)session.PersonalProfile.DeepClone();LastMemories=(JsonArray)memories.DeepClone();await onReply(StreamedReply);if(Fail)throw new ServiceException("测试断线");return (JsonObject)Response.DeepClone();}
    }
}
public sealed class PetProtocolTests
{
    [Theory]
    [InlineData("{\"reply\":\"你好", "你好")]
    [InlineData("{\"reply\":\"你好\\", "你好")]
    [InlineData("{\"reply\":\"你好\\u4", "你好")]
    [InlineData("{\"reply\":\"A\\nB\",\"fields\":{}", "A\nB")]
    public void StreamingReplyHandlesChunkBoundariesAndEscapes(string input,string expected)=>Assert.Equal(expected,PetQuestions.PartialReply(input));
    [Fact]
    public void UnknownModelFieldsNeverBecomeApplicationState()
    {var s=new PetSession();PetQuestions.Apply(s,new JsonObject{["destination"]="悉尼",["consent"]=true,["apiKey"]="ignored"});Assert.Single(s.Fields);}
}
