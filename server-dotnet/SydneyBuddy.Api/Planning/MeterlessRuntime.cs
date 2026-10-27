using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// Backend-only provider configuration. Tokens never enter a mobile contract.
public sealed class PlanningAgentSettings(IConfiguration configuration)
{
    public string Provider => configuration["PlanningAgent:Provider"] ?? "nvidia";
    public string Model => configuration["Meterless:Model"] ?? "meterless/auto";
    public string Token => (configuration["meterless_runtime_token"] ?? configuration["Meterless:Token"] ?? "").Trim();
    public bool Configured => Provider == "meterless" && Token.Length > 0 && Token != "rt-local";
    public Uri BaseUri => Normalize(configuration["Meterless:BaseUrl"] ?? "http://127.0.0.1:8088/v1",
        configuration["Meterless:AllowInsecureHttp"] == "true");

    public static Uri Normalize(string input, bool allowInsecureHttp = false)
    {
        var text = input.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 ||
            uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            !(uri.Scheme == "https" || uri.Scheme == "http" && (uri.IsLoopback || allowInsecureHttp)))
            throw new ServiceException("Meterless 連線設定無效；遠端服務請使用 HTTPS，Token 另存於後端設定。");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path is not ("" or "/v1" or "/v1/chat/completions" or "/v1/models" or "/healthz"))
            throw new ServiceException("Meterless 連線路徑無效，請使用 Runtime 的 /v1 網址。");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/v1/");
    }
}

/// One OpenAI-compatible Meterless call boundary; no redirects, retries, or provider fallback.
public sealed class MeterlessRuntime(HttpClient client, PlanningAgentSettings settings)
{
    public async Task<JsonObject> StatusAsync(CancellationToken ct)
    {
        if (!settings.Configured) return new() { ["status"] = "not_configured", ["models"] = new JsonArray() };
        try
        {
            var body = await SendAsync("models", null, 10, ct);
            var models = body.Array("data")?.OfType<JsonObject>().Select(x => x.Str("id"))
                .Where(x => x.StartsWith("meterless/", StringComparison.Ordinal)).Distinct().ToArray() ?? [];
            return new() { ["status"] = models.Length > 0 ? "online" : "offline",
                ["models"] = new JsonArray(models.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()) };
        }
        catch (MeterlessAuthorizationException) { return new() { ["status"] = "unauthorized", ["models"] = new JsonArray() }; }
        catch (ServiceException) { return new() { ["status"] = "offline", ["models"] = new JsonArray() }; }
    }

    public async Task<JsonObject> CompleteAsync(string skill, JsonObject evidence, CancellationToken ct)
    {
        var status = await StatusAsync(ct);
        if (status.Str("status") == "unauthorized") throw new ServiceException("Meterless 授權失敗，請在後端更新 Runtime Token；原行程保持不變。");
        if (status.Str("status") != "online") throw new ServiceException("Meterless 尚未連線或未設定；原行程保持不變。");
        if (!status.Array("models")!.Any(x => x!.GetValue<string>() == settings.Model))
            throw new ServiceException("Meterless 未提供所選規劃模式，請檢查後端 Model 設定。");
        try { return await SendAsync("chat/completions", new JsonObject {
            ["model"] = settings.Model, ["stream"] = false, ["temperature"] = 0.2, ["max_tokens"] = 4096,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = skill },
                new JsonObject { ["role"] = "user", ["content"] = evidence.ToJsonString() })
        }, 180, ct); }
        catch (MeterlessAuthorizationException) { throw new ServiceException("Meterless 授權失敗，請在後端更新 Runtime Token；原行程保持不變。"); }
    }

    private async Task<JsonObject> SendAsync(string path, JsonObject? payload, int seconds, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            using var request = new HttpRequestMessage(payload is null ? HttpMethod.Get : HttpMethod.Post, new Uri(settings.BaseUri, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
            request.Headers.Add("meterless-app", "sydneybuddy");
            if (payload is not null) {
                request.Headers.Add("meterless-scope", "user");
                request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            }
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new MeterlessAuthorizationException();
            if (!response.IsSuccessStatusCode) throw new ServiceException("Meterless 暫時無法規劃，原行程保持不變。");
            // Bound generated output; never include provider response bodies in errors or logs.
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var data = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, deadline.Token)) > 0) {
                if (data.Length + count > 262144) throw new ServiceException("Meterless 回應過大，已拒絕規劃結果。");
                data.Write(buffer, 0, count);
            }
            return JsonNode.Parse(data.ToArray()) as JsonObject ?? throw new JsonException();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ServiceException("Meterless 查詢逾時，需求已保留。"); }
        catch (Exception e) when (e is HttpRequestException or IOException or JsonException or FormatException) { throw new ServiceException("Meterless 連線失敗或回應無效，需求已保留。"); }
    }
    private sealed class MeterlessAuthorizationException : Exception;
}
