using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>外部 JSON 请求契约；the shared boundary for provider calls and deterministic test doubles.</summary>
public interface IExternalJsonClient
{
    Task<JsonObject> FetchAsync(string url, JsonNode? payload = null,
        IReadOnlyDictionary<string, string>? headers = null, int timeoutSeconds = 25,
        CancellationToken cancellationToken = default);
}

/// <summary>调用外部 JSON 服务并遮蔽错误详情；calls providers without exposing private request/response data.</summary>
public sealed class ExternalJsonClient(HttpClient client, PaidServicePolicy? paidServices = null) : IExternalJsonClient
{
    public async Task<JsonObject> FetchAsync(string url, JsonNode? payload = null,
        IReadOnlyDictionary<string, string>? headers = null, int timeoutSeconds = 25,
        CancellationToken cancellationToken = default)
    {
        paidServices?.RequireAllowed();
        // 分开处理用户取消与服务超时；preserve caller cancellation while translating provider deadlines.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var request = new HttpRequestMessage(payload is null ? HttpMethod.Get : HttpMethod.Post, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (payload is not null)
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        if (headers is not null)
            foreach (var (name, value) in headers)
                if (!request.Headers.TryAddWithoutValidation(name, value))
                    throw new ServiceException("外部服务请求配置无效。");

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            // 响应正文可能包含密钥或偏好；report only the status, never the provider body.
            if (!response.IsSuccessStatusCode)
                throw new ServiceException($"外部服务返回 HTTP {(int)response.StatusCode}，请检查权限、额度和服务配置。");
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            return await JsonNode.ParseAsync(stream, cancellationToken: linked.Token) as JsonObject
                   ?? throw new ServiceException("外部服务连接失败或响应无效；未将其标记为实时数据。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ServiceException("外部服务连接失败或响应无效；未将其标记为实时数据。");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
            throw new ServiceException("外部服务连接失败或响应无效；未将其标记为实时数据。");
        }
    }
}
