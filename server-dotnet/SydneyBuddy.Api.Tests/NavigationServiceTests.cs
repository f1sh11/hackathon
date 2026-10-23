using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace SydneyBuddy.Api.Tests;

public sealed class NavigationServiceTests
{
    private static IConfiguration Config(string? endpoint = "https://routing.example/route") => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string,string?> { ["Navigation:ValhallaUrl"] = endpoint }).Build();
    private static JsonObject Input() => JsonNode.Parse("""{"locations":[{"lat":-33.8611,"lon":151.2126},{"lat":-33.8568,"lon":151.2153}]}""")!.AsObject();
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-url")]
    [InlineData("http://routing.example/route")]
    public async Task UnconfiguredOrInsecureUpstreamNeverCallsNetwork(string? endpoint)
    {
        var handler = new Handler(); var service = new NavigationService(new HttpClient(handler), Config(endpoint));
        Assert.False(service.Configured);
        await Assert.ThrowsAsync<ServiceException>(() => service.RouteAsync(Input(), default));
        Assert.Equal(0, handler.Calls);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"locations\":[]}")]
    [InlineData("{\"locations\":[{\"lat\":91,\"lon\":0},{\"lat\":0,\"lon\":0}]}")]
    [InlineData("{\"locations\":[{\"lat\":\"1\",\"lon\":0},{\"lat\":0,\"lon\":0}]}")]
    public async Task InvalidCoordinatesNeverCallUpstream(string json)
    {
        var handler = new Handler(); var service = new NavigationService(new HttpClient(handler), Config());
        await Assert.ThrowsAsync<ServiceException>(() => service.RouteAsync(JsonNode.Parse(json)!.AsObject(), default));
        Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task WalkingProfileAndProviderAreServerOwnedAndStopsKeepTheirOrder()
    {
        var handler = new Handler(); var service = new NavigationService(new HttpClient(handler), Config());
        var input = Input(); input["url"] = "https://untrusted.example"; input["costing"] = "auto";
        input.Array("locations")!.Add(new JsonObject { ["lat"] = -33.87, ["lon"] = 151.21 });
        handler.Response = """{"code":"Ok","routes":[{"geometry":"encoded","legs":[{},{}]}]}""";
        var result = await service.RouteAsync(input, default);
        Assert.Equal("Ok", result.Str("code"));
        Assert.Equal("https://routing.example/route", handler.Url);
        var sent = JsonNode.Parse(handler.Body!)!.AsObject();
        Assert.Equal("pedestrian", sent.Str("costing"));
        Assert.Equal("osrm", sent.Str("format"));
        Assert.Equal("polyline6", sent.Str("shape_format"));
        Assert.True(sent.Bool("voice_instructions"));
        Assert.False(sent.ContainsKey("url"));
        Assert.Equal(-33.8611, sent.Array("locations")![0]!.Number("lat"));
        Assert.Equal(-33.8568, sent.Array("locations")![1]!.Number("lat"));
        Assert.Equal(-33.87, sent.Array("locations")![2]!.Number("lat"));
    }
    [Theory]
    [InlineData("{\"code\":\"NoRoute\",\"routes\":[]}")]
    [InlineData("{\"code\":\"Ok\",\"routes\":[{\"geometry\":\"\",\"legs\":[{}]}]}")]
    [InlineData("{\"code\":\"Ok\",\"routes\":[{\"geometry\":\"abc\",\"legs\":[]}]}")]
    [InlineData("not-json")]
    public async Task InvalidRouteCannotMasqueradeAsNavigable(string response)
    {
        var handler = new Handler { Response = response };
        await Assert.ThrowsAsync<ServiceException>(() => new NavigationService(new HttpClient(handler), Config()).RouteAsync(Input(), default));
    }
    [Fact]
    public async Task RequestCancellationPropagates()
    {
        var handler = new Handler(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NavigationService(new HttpClient(handler), Config()).RouteAsync(Input(), cancellation.Token));
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls; public string? Body; public string? Url;
        public string Response = """{"code":"Ok","routes":[{"geometry":"abc","legs":[{}]}]}""";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            Url = request.RequestUri!.ToString(); Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response, Encoding.UTF8,"application/json") };
        }
    }
}
