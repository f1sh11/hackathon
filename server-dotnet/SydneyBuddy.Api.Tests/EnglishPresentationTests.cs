using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SydneyBuddy.Api.Tests;

public sealed class EnglishPresentationTests
{
    private static JsonObject View(PetSession s)=>JsonSerializer.SerializeToNode(PetQuestions.View(s),JsonExtensions.Options)!.AsObject();
    private static void English(string text)=>Assert.DoesNotMatch(@"[\u3400-\u9fff]",text);
    [Fact]
    public void EnglishViewKeepsStoredMessagesFactsEvidenceAndIds() {
        var option=new PetQuickReply("option","想歇一歇","mood_rest");
        var question=new PetMessage("question","assistant",KokoCompanion.Greeting,QuickReplies:[option]);
        var s=new PetSession{ResponseLanguage="en",Dialogue=new(){CompanionVersion=1},Messages=[new("user","user","我喜欢自然"),question]};
        s.Fields["notes"]="我喜欢自然";s.Preferences.Add(new(){Key="interests",Value="自然",Evidence="我喜欢自然",SourceMessageId="user"});
        var original=JsonSerializer.Serialize(s,JsonExtensions.Options);
        var view=View(s);var shown=view.Array("messages")!.Last()!.AsObject();
        English(shown.Str("text"));var shownOption=shown.Array("quickReplies")!.First()!.AsObject();English(shownOption.Str("text"));
        Assert.Equal(option.Id,shownOption.Str("id"));Assert.Equal(question.Id,shown.Str("id"));
        Assert.Equal("我喜欢自然",view.Array("messages")![0]!.Str("text"));Assert.Equal("我喜欢自然",view.Object("fields")!.Str("notes"));
        Assert.Equal(original,JsonSerializer.Serialize(s,JsonExtensions.Options));
        var selected=KokoDialogue.ResolveChoice(s,new(){["questionId"]=question.Id,["optionId"]=option.Id,["message"]=shownOption.Str("text")});
        Assert.Equal(option,selected);Assert.Equal(option.Text,s.CurrentChoice!.OptionText);
        Assert.Throws<ServiceException>(()=>KokoDialogue.ResolveChoice(s,new(){["questionId"]="stale",["optionId"]=option.Id,["message"]=shownOption.Str("text")}));
    }
    [Theory]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void ChineseLanguageSelectionIsNotForcedToEnglish(string language) {
        var s=new PetSession{Messages=[new("q","assistant",KokoCompanion.Greeting)]};PetResponseLanguage.Apply(s,new(){["language"]=language});
        Assert.Equal(language,s.ResponseLanguage);Assert.Equal(KokoCompanion.Greeting,View(s).Array("messages")![0]!.Str("text"));
    }
    [Fact]
    public void UnknownHistoricalProseAndUserNamesArePreserved() {
        var s=new PetSession{ResponseLanguage="en",Messages=[new("unknown","assistant","这是一次独特的描述、不会自动重写。"),new("name","assistant","小林，咱们先找一点这趟旅行的灵感。") ]};
        var messages=View(s).Array("messages")!;
        Assert.Equal(s.Messages[0].Text,messages[0]!.Str("text"));Assert.StartsWith("小林,",messages[1]!.Str("text"));Assert.DoesNotContain("咱们",messages[1]!.Str("text"));
    }
    [Fact]
    public void NewBudgetAndRememberedHabitsRenderWithoutChinese() {
        foreach(var text in new[]{"这次按每人每天 0 AUD 活动预算来安排，不含住宿、交通和全部餐饮。\n资料准备齐啦！可以直接生成行程，也可以继续聊或单独修改。","我记得你确认过「每天约 6000 步」。这次也参考它吗？想换换也可以。"}) {
            var s=new PetSession{ResponseLanguage="en",Messages=[new("q","assistant",text)]};English(View(s).Array("messages")![0]!.Str("text"));
        }
        var zero=new PetSession();PetQuestions.Apply(zero,new(){["budget"]="0",["budgetCurrency"]="AUD",["budgetBasis"]="perPersonPerDay"});Assert.True(PetQuestions.Answered(zero,"budget"));
    }
    [Theory]
    [InlineData("NOT SURE YET")][InlineData("Add later")][InlineData("Not decided yet")]
    public void EnglishUnknownValuesDoNotCompleteRequirements(string value)=>Assert.False(PetQuestions.Known(value));
    [Fact]
    public void EnglishHabitEvidenceAndCanonicalStorageSurvive() {
        var fields=new JsonObject{["energy"]="low",["rhythm"]="relaxed",["pace"]="8000",["queueTolerance"]="20",["interests"]="Nature"};
        PetExplicitFacts.FilterUnsupportedHabits("I like nature, a relaxed pace and 8,000 steps. I have low energy and can wait 20 minutes.",fields);Assert.Equal(5,fields.Count);
        var s=new PetSession();PetQuestions.Apply(s,fields);Assert.Equal("低精力",s.Fields["energy"]);Assert.Equal("轻松随意",s.Fields["rhythm"]);
        PetExplicitFacts.FilterUnsupportedHabits("Sydney tomorrow for three days.",fields);Assert.Empty(fields);
    }
    [Theory]
    [InlineData("I am tired",true)][InlineData("I am tired of museums",false)][InlineData("I dont feel tired",false)][InlineData("I do not feel tired",false)][InlineData("I'm not very tired",false)][InlineData("I don't need a rest",false)]
    public void EnglishRestRecognitionRespectsNegation(string message,bool expected)=>Assert.Equal(expected,KokoCompanion.NeedsRest(message));
    [Theory]
    [InlineData("Don't save this: I like nature")][InlineData("Dont save this: I like nature")][InlineData("Only for this trip, I prefer quiet places")][InlineData("Do not remember my preference")]
    public void EnglishRefusalsCannotBecomeLongTermCandidates(string message) {
        var evidence=message;
        var s=new PetSession();var result=new JsonObject{["preferences"]=new JsonArray(new JsonObject{["key"]="interests",["value"]="Nature",["scope"]="long_term",["evidence"]=evidence})};
        TravelProfiling.Apply(s,result,message,"request");Assert.Single(s.Preferences);Assert.All(s.Preferences,p=>Assert.Equal("trip",p.Scope));Assert.False(TravelProfiling.AllowLegacySuggestion(message));
    }
}

[Trait("Category","Integration")]
public sealed class EnglishPresentationHttpTests(ApiFixture fixture):IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ResumedEnglishChoiceCanBePostedWithOriginalStableIds() {
        using var factory=fixture.CreateFactory();using var client=factory.CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var created=await client.PostAsJsonAsync("/pet/sessions",new{language="zh-Hans",profilingVersion=2,companionVersion=1,hasMetKoko=true});created.EnsureSuccessStatusCode();
        var original=(await created.Content.ReadFromJsonAsync<JsonObject>())!;var id=original.Str("id");
        using var read=new HttpRequestMessage(HttpMethod.Get,"/pet/sessions/"+id);read.Headers.Add("X-App-Language","en");
        var response=await client.SendAsync(read);response.EnsureSuccessStatusCode();var english=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var question=english.Array("messages")!.Last()!.AsObject();var option=question.Array("quickReplies")!.First()!.AsObject();
        Assert.DoesNotMatch(@"[\u3400-\u9fff]",option.Str("text"));Assert.Equal(original.Array("messages")!.Last()!.Str("id"),question.Str("id"));
        var turn=await client.PostAsJsonAsync("/pet/sessions/"+id+"/turn",new{language="en",requestId=Guid.NewGuid().ToString(),message=option.Str("text"),questionId=question.Str("id"),optionId=option.Str("id")});turn.EnsureSuccessStatusCode();
        var events=(await turn.Content.ReadAsStringAsync()).Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>JsonNode.Parse(line)!.AsObject()).ToArray();
        Assert.DoesNotContain(events,e=>e.Str("type")=="error");Assert.Contains(events,e=>e.Str("type")=="done");
        Assert.All(events.Where(e=>e.Str("type")=="reply"),e=>Assert.DoesNotMatch(@"[\u3400-\u9fff]",e.Str("text")));
    }
    [Theory]
    [InlineData("Today",0)][InlineData("Tomorrow",1)]
    public async Task LegacyEnglishDateChoiceUsesRealDate(string choice,int offset) {
        using var factory=fixture.CreateFactory();using var client=factory.CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var created=(await (await client.PostAsJsonAsync("/pet/sessions",new{language="en"})).Content.ReadFromJsonAsync<JsonObject>())!;
        var response=await client.PostAsJsonAsync("/pet/sessions/"+created.Str("id")+"/turn",new{language="en",requestId=Guid.NewGuid().ToString(),message=choice,field="date",value=choice});
        var events=(await response.Content.ReadAsStringAsync()).Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>JsonNode.Parse(line)!.AsObject()).ToArray();
        var session=events.Last().Object("session")!;
        var expected=TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow,"Australia/Sydney").Date.AddDays(offset).ToString("yyyy-MM-dd");
        Assert.Equal(expected,session.Object("fields")!.Str("date"));
    }
}
