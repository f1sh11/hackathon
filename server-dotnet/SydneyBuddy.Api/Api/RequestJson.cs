using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>解析 API 的对象请求体；parses bounded JSON object bodies without exposing parser internals.</summary>
public static class RequestJson
{
    public static async Task<JsonObject> ReadAsync(HttpContext context)
    {
        if (context.Request.ContentLength is 0) throw new JsonException();
        return await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted) as JsonObject
            ?? throw new JsonException();
    }
}
