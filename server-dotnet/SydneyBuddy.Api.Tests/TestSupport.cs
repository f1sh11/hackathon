using System.Text.Json.Nodes;

namespace SydneyBuddy.Api.Tests;

/// <summary>隔离测试数据与仓库定位；shared fixtures never read team credentials.</summary>
internal static class TestSupport
{
    public static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "server", "places.json"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate repository server/places.json for migration tests.");
    }

    public static JsonObject Profile(int days = 1) => new()
    {
        ["date"] = "2026-10-24", ["days"] = days, ["budget"] = 100,
        ["maxSteps"] = 10000, ["startMinute"] = 600, ["endMinute"] = 1080,
        ["interests"] = new JsonArray("美食", "风景")
    };
}

internal sealed record FetchCall(string Url, JsonNode? Payload, IReadOnlyDictionary<string, string> Headers, int TimeoutSeconds);

/// <summary>记录外部请求并返回 JSON；provider fake exercises the real serialization boundary.</summary>
internal sealed class FakeExternal : IExternalJsonClient
{
    public List<FetchCall> Calls { get; } = [];
    public Func<FetchCall, JsonObject> Respond { get; set; } = _ => throw new InvalidOperationException("Unexpected external API call.");

    public Task<JsonObject> FetchAsync(string url, JsonNode? payload = null,
        IReadOnlyDictionary<string, string>? headers = null, int timeoutSeconds = 25, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var call = new FetchCall(url, payload?.DeepClone(), headers ?? new Dictionary<string, string>(), timeoutSeconds);
        Calls.Add(call);
        return Task.FromResult(JsonNode.Parse(Respond(call).ToJsonString())!.AsObject());
    }
}

/// <summary>隔离大部分 HTTP 测试的记忆状态；a process-free memory double for API contract tests.</summary>
internal sealed class FakeMemory : IMemoryService
{
    private readonly Dictionary<string, string> _records = new();
    public Task<JsonObject> RunAsync(string action, JsonObject arguments, CancellationToken ct = default)
    {
        lock (_records)
        {
            JsonObject result;
            switch (action)
            {
                case "add":
                    var id = Guid.NewGuid().ToString();
                    _records[id] = arguments.Str("content");
                    result = new JsonObject { ["id"] = id };
                    break;
                case "stage":
                    result = new JsonObject { ["candidates"] = new JsonArray() };
                    break;
                case "discard_session":
                    result = new JsonObject { ["deleted"] = true };
                    break;
                case "delete":
                    _records.Remove(arguments.Str("id"));
                    result = new JsonObject { ["deleted"] = true };
                    break;
                case "query":
                    result = new JsonObject { ["memories"] = new JsonArray(), ["context"] = "" };
                    break;
                default: throw new InvalidOperationException("Unexpected memory action.");
            }
            return Task.FromResult(result);
        }
    }
}
