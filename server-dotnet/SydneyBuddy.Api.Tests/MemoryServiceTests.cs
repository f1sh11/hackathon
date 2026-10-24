using System.Text.Json.Nodes;

namespace SydneyBuddy.Api.Tests;

/// <summary>通过真实 Node 引擎验证授权记忆生命周期；uses the unchanged official H-MEM implementation.</summary>
public sealed class MemoryServiceTests
{
    [Fact]
    public async Task CandidateMiningRetainsEvidenceButOnlyConfirmedLongTermRecordsAreRecalled()
    {
        var directory=Path.Combine(Path.GetTempPath(),"sydney-hmem-candidates-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var service=new MemoryService(new ProjectPaths(TestSupport.RepositoryRoot(),directory));
            var candidate=new JsonObject{["id"]="c1",["key"]="early_start_exception",["value"]="sunrise",["scope"]="long_term",["status"]="awaiting_confirmation",["source"]="user_statement",["sourceMessageId"]="message-1",["confidence"]=0.87,["evidence"]="我讨厌早起，但为了看日出可以。"};
            var staged=await service.RunAsync("stage",new JsonObject{["sessionId"]="test-session",["candidates"]=new JsonArray(candidate.DeepClone())});
            var working=Assert.Single(staged.Array("candidates")!);Assert.Equal("working",working!.Str("layer"));Assert.Equal("user_statement",working.Str("source"));Assert.Equal(0.87,working.Number("confidence"));Assert.Contains("看日出",working.Str("content"));
            var hidden=await service.RunAsync("query",new JsonObject{["query"]=candidate.ToJsonString()});Assert.Empty(hidden.Array("memories")!);
            const string phrase="travel preference sunrise early start exception test";
            candidate["status"]="confirmed";
            var saved=await service.RunAsync("add",new JsonObject{["content"]=phrase,["travelDna"]=candidate.DeepClone()});Assert.Equal("long_term",saved.Str("layer"));
            var recalled=await service.RunAsync("query",new JsonObject{["query"]=phrase});Assert.Contains(recalled.Array("memories")!,x=>x!.Object("memory")!.Str("id")==saved.Str("id"));
            await service.RunAsync("discard_session",new JsonObject{["sessionId"]="test-session"});
            var remaining=await service.RunAsync("list",new JsonObject());Assert.Single(remaining.Array("memories")!);
            await service.RunAsync("delete",new JsonObject{["id"]=saved.Str("id")});
        } finally {Directory.Delete(directory,true);}
    }
    [Fact]
    public async Task ConfirmedPreferenceCanBeRecalledAndRevokedAcrossProcesses()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sydney-hmem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var paths = new ProjectPaths(TestSupport.RepositoryRoot(), directory);
            var service = new MemoryService(paths);
            const string phrase = "travel preference Sydney art quiet museums test-only";
            var added = await service.RunAsync("add", new JsonObject { ["content"] = phrase });
            var id = added.Str("id");
            Assert.False(string.IsNullOrWhiteSpace(id));
            // 每次调用启动新进程；successful recall therefore also proves disk persistence.
            var recalled = await service.RunAsync("query", new JsonObject { ["query"] = phrase });
            Assert.Contains(recalled.Array("memories")!, result => result!.Object("memory")!.Str("id") == id);
            await service.RunAsync("delete", new JsonObject { ["id"] = id });
            var revoked = await service.RunAsync("query", new JsonObject { ["query"] = phrase });
            Assert.DoesNotContain(revoked.Array("memories")!, result => result!.Object("memory")!.Str("id") == id);
            Assert.True(Directory.Exists(paths.HmemDirectory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
