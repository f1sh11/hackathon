using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace SydneyBuddy.Api.Tests;

public sealed class PetEnglishPresenterTests
{
    private static JsonObject View(PetSession s)=>JsonSerializer.SerializeToNode(PetQuestions.View(s),JsonExtensions.Options)!.AsObject();
    internal const string Source="我们可以沿着海边慢慢走，你喜欢咖啡吗？";
    internal const string English="We can take an easy waterfront walk. Do you like coffee?";
    internal sealed class Translator:IPetEnglishTranslator {
        public int Calls;public bool Fails;
        public Task<string[]> TranslateAsync(string[] source,CancellationToken ct) {
            Calls++;if(Fails)throw new HttpRequestException();
            return Task.FromResult(source.Select(s=>s==Source ? English:s=="我想先在海边喝杯拿铁" ? "I'd like a waterfront latte first.":"English display").ToArray());
        }
    }
    private static PetEnglishPresenter Presenter(Translator translator)=>new(translator,NullLogger<PetEnglishPresenter>.Instance);
    [Fact] public async Task HistoricalDisplayIsCachedWithoutChangingOriginalFactsOrEvidence() {
        var t=new Translator();var presenter=Presenter(t);
        var session=new PetSession{ResponseLanguage="en",Messages=[new("user","user","我喜欢咖啡"),new("reply","assistant",Source,QuickReplies:[new("option","我想先在海边喝杯拿铁")])]};
        session.Fields["date"]="2026-10-30";session.Preferences.Add(new(){Key="interests",Evidence="我喜欢咖啡",Value="咖啡",Status="awaiting_confirmation"});
        var before=JsonSerializer.Serialize(new{session.Messages,session.Fields,session.Preferences});
        await presenter.PrepareAsync(session,default);
        Assert.Equal(English,View(session).Array("messages")!.Last()!.Str("text"));
        Assert.Equal(before,JsonSerializer.Serialize(new{session.Messages,session.Fields,session.Preferences}));
        var restored=JsonSerializer.Deserialize<PetSession>(JsonSerializer.Serialize(session))!;
        await presenter.PrepareAsync(restored,default);Assert.Equal(1,t.Calls);
        Assert.Equal("option",View(restored).Array("messages")!.Last()!.Array("quickReplies")![0]!.Str("id"));
        Assert.Equal("我喜欢咖啡",restored.Messages[0].Text);Assert.Empty(restored.CompletedRequests);
    }
    [Fact] public async Task TranslationFailureIsEnglishAndRetriableWithoutLosingOriginal() {
        var t=new Translator{Fails=true};var s=new PetSession{ResponseLanguage="en",Messages=[new("reply","assistant",Source)]};
        await Presenter(t).PrepareAsync(s,default);
        Assert.Equal(PetEnglishPresenter.Unavailable,View(s).Array("messages")![0]!.Str("text"));Assert.NotEmpty(View(s).Str("presentationWarning"));Assert.Equal(Source,s.Messages[0].Text);Assert.Empty(s.EnglishDisplay);
        t.Fails=false;await Presenter(t).PrepareAsync(s,default);Assert.Equal(English,View(s).Array("messages")![0]!.Str("text"));
    }
    private sealed class TranslationHandler:HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            var body=JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            var text=body.Array("messages")!.Last()!.Str("content");
            Assert.Contains(Source,text);Assert.DoesNotContain("\\u",text);
            return new(System.Net.HttpStatusCode.OK){Content=JsonContent.Create(new{choices=new[]{new{message=new{content=JsonSerializer.Serialize(new{translations=new[]{English}})}}}})};
        }
    }
    [Fact] public async Task ProviderReceivesReadableChineseRatherThanEscapedCharacterCodes() {
        var folder=Path.Combine(Path.GetTempPath(),"koko-display-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try {
            await File.WriteAllTextAsync(Path.Combine(folder,".secrets.json"),"{\"NVIDIA_API_KEY\":\"test-key\"}");
            using var client=new HttpClient(new TranslationHandler());
            var translator=new PetEnglishTranslator(client,new SecretStore(new ProjectPaths(TestSupport.RepositoryRoot(),folder)));
            Assert.Equal(new[]{English},await translator.TranslateAsync([Source],default));
        }finally{Directory.Delete(folder,true);}
    }
    [Fact] public async Task ChineseModeDoesNotTranslateAndInvalidNumbersCannotEnterCache() {
        var t=new Translator();var presenter=Presenter(t);var s=new PetSession{ResponseLanguage="zh-Hans",Messages=[new("r","assistant",Source)]};
        await presenter.PrepareAsync(s,default);Assert.Equal(0,t.Calls);
        s.ResponseLanguage="en";s.Messages=[new("r","assistant","大约需要 45 分钟")];await presenter.PrepareAsync(s,default);
        Assert.Empty(s.EnglishDisplay);Assert.Equal(PetEnglishPresenter.Unavailable,View(s).Array("messages")![0]!.Str("text"));
    }
}

[Trait("Category","Integration")]
public sealed class PetEnglishPresenterHttpTests(ApiFixture fixture):IClassFixture<ApiFixture>
{
    private sealed class Model:IPetModel {
        public Task<JsonObject> RespondAsync(PetSession s,string message,JsonArray memories,Func<string,Task> onReply,CancellationToken ct)=>Task.FromResult(new JsonObject{
            ["reply"]=PetEnglishPresenterTests.Source,["fields"]=new JsonObject{["date"]="2026-10-30"},["quickReplies"]=new JsonArray(new JsonObject{["text"]="我想先在海边喝杯拿铁"})});
    }
    [Fact] public async Task TurnResumeAndTranslatedChoicePreserveIdsAndDate() {
        var translator=new PetEnglishPresenterTests.Translator();
        using var factory=fixture.CreateFactory().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<IPetModel>();s.AddSingleton<IPetModel>(new Model());s.RemoveAll<IPetEnglishTranslator>();s.AddSingleton<IPetEnglishTranslator>(translator);}));
        using var client=factory.CreateClient(new(){BaseAddress=new Uri("http://localhost")});
        var created=(await (await client.PostAsJsonAsync("/pet/sessions",new{language="en",profilingVersion=2,hasMetKoko=true})).Content.ReadFromJsonAsync<JsonObject>())!;
        var id=created.Str("id");
        var response=await client.PostAsJsonAsync("/pet/sessions/"+id+"/turn",new{language="en",conversationPhase="personalize",requestId=Guid.NewGuid().ToString(),message="Change the date to 2026-10-30."});
        var events=(await response.Content.ReadAsStringAsync()).Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(x=>JsonNode.Parse(x)!.AsObject()).ToArray();
        Assert.DoesNotContain(events,x=>x.Str("type")=="error");Assert.Equal(PetEnglishPresenterTests.English,events.First(x=>x.Str("type")=="reply").Str("text"));
        var done=events.Last().Object("session")!;Assert.Equal("2026-10-30",done.Object("fields")!.Str("date"));
        using var request=new HttpRequestMessage(HttpMethod.Get,"/pet/sessions/"+id);request.Headers.Add("X-App-Language","en");
        var resumed=(await (await client.SendAsync(request)).Content.ReadFromJsonAsync<JsonObject>())!;
        var question=resumed.Array("messages")!.Last()!.AsObject();var option=question.Array("quickReplies")![0]!.AsObject();
        Assert.Equal(PetEnglishPresenterTests.English,question.Str("text"));Assert.Equal(1,translator.Calls);
        var selected=await client.PostAsJsonAsync("/pet/sessions/"+id+"/turn",new{language="en",conversationPhase="personalize",requestId=Guid.NewGuid().ToString(),questionId=question.Str("id"),optionId=option.Str("id"),message=option.Str("text")});
        Assert.DoesNotContain("\"type\":\"error\"",await selected.Content.ReadAsStringAsync());
        var patched=(await (await client.PatchAsJsonAsync("/pet/sessions/"+id,new{language="en",fields=new{budget="60",budgetCurrency="AUD",budgetBasis="perPersonPerDay"}})).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("60",patched.Object("fields")!.Str("budget"));
        Assert.All(patched.Array("messages")!.OfType<JsonObject>().Where(x=>x.Str("role")=="assistant"),x=>Assert.DoesNotMatch(@"[\u3400-\u9fff]",x.Str("text")));
    }
}
