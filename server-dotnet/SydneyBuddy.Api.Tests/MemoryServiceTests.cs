using System.Text.Json.Nodes;

namespace SydneyBuddy.Api.Tests;

/// <summary>通过真实 Node 引擎验证授权记忆生命周期；uses the official H-MEM engine with the documented Node compatibility patch.</summary>
public sealed class MemoryServiceTests
{
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
