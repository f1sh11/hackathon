using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace SydneyBuddy.Api.Tests;

[Trait("Category","Integration")]
public sealed class PetConversationTests(ApiFixture fixture):IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("relaxed","relaxed")]
    [InlineData("excited","excited")]
    [InlineData("angry","curious")]
    [InlineData("","curious")]
    public void ReplyEmotionOnlyAcceptsSafeKnownStates(string input,string expected)=>Assert.Equal(expected,PetEmotion.Normalize(input));
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
        var chatResult=await Turn(client,id,new {message="我喜欢艺术和慢节奏",requestId=Guid.NewGuid().ToString()});
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
        await Turn(client,id,new{message="我喜欢安静的餐厅",requestId=Guid.NewGuid().ToString()});
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
        public JsonArray LastMemories {get;private set;}=[];
        public async Task<JsonObject> RespondAsync(PetSession session,string message,JsonArray memories,Func<string,Task> onReply,CancellationToken ct)
        {LastMemories=(JsonArray)memories.DeepClone();await onReply("收到");if(Fail)throw new ServiceException("测试断线");return (JsonObject)Response.DeepClone();}
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
