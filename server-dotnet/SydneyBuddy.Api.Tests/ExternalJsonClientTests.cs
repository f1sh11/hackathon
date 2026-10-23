using System.Net;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api.Tests;

/// <summary>验证外部服务失败的隐私边界；provider error bodies must stay out of API errors.</summary>
public sealed class ExternalJsonClientTests
{
    [Fact]
    public async Task ProviderErrorReportsStatusWithoutEchoingResponseBody()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("private-provider-key-and-preferences")
        }));
        var error = await Assert.ThrowsAsync<ServiceException>(() => new ExternalJsonClient(client).FetchAsync("https://provider.example/resource"));
        Assert.Contains("403", error.Message);
        Assert.DoesNotContain("private-provider", error.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    public async Task InvalidProviderResponseCannotBecomeSuccessfulEvidence(string body)
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        await Assert.ThrowsAsync<ServiceException>(() => new ExternalJsonClient(client).FetchAsync("https://provider.example/resource"));
    }

    [Fact]
    public async Task PayloadUsesPostWithSeparateCredentialHeader()
    {
        string? method = null, authorization = null, content = null;
        using var client = new HttpClient(new StubHandler(async request =>
        {
            method = request.Method.Method;
            authorization = request.Headers.Authorization?.ToString();
            content = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }));
        var actual = await new ExternalJsonClient(client).FetchAsync("https://provider.example/resource",
            new JsonObject { ["value"] = 42 }, new Dictionary<string, string> { ["Authorization"] = "Bearer fixture-token" });
        Assert.True(actual.Bool("ok"));
        Assert.Equal("POST", method);
        Assert.Equal("Bearer fixture-token", authorization);
        Assert.Equal(42, JsonNode.Parse(content!)!.Int("value"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public async Task PaidServicePolicyGuardsTransportBeforeAnyRequest(string? setting,bool allowed)
    {
        var called=false;
        using var client=new HttpClient(new StubHandler(_ => {
            called=true;
            return new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent("{}")};
        }));
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["AllowPaidServices"]=setting
        }).Build();
        var transport=new ExternalJsonClient(client,new PaidServicePolicy(config));
        if(allowed) await transport.FetchAsync("https://provider.example/resource");
        else await Assert.ThrowsAsync<ServiceException>(()=>transport.FetchAsync("https://provider.example/resource"));
        Assert.Equal(allowed,called);
    }

    /// <summary>以本机处理器模拟 HTTP；a handler removes network variability from transport tests.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : this(request => Task.FromResult(respond(request))) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
