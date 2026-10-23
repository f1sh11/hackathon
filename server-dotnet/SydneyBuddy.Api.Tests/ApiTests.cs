using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace SydneyBuddy.Api.Tests;

/// <summary>使用独立 PostgreSQL schema 验证真实 API；integration tests never replace the database with an in-memory store.</summary>
[Trait("Category", "Integration")]
public sealed class ApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task HealthAndCatalogPreserveMobileContracts()
    {
        var health = await fixture.Client.GetFromJsonAsync<JsonObject>("/health");
        Assert.NotNull(health);
        Assert.Equal("ok", health.Str("status"));
        Assert.False(health.Bool("nvidiaConfigured"));
        Assert.False(health.Bool("googleConfigured"));
        Assert.False(health.ContainsKey("NVIDIA_API_KEY"));
        Assert.False(health.ContainsKey("GOOGLE_SERVER_KEY"));
        var mobile = await fixture.Client.GetFromJsonAsync<JsonArray>("/catalog");
        Assert.Equal(8, mobile!.Count);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/assets/test.js")]
    [InlineData("/api/health")]
    public async Task RemovedWebRoutesReturnJsonNotFound(string path)
    {
        var response = await fixture.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrEmpty((await response.Content.ReadFromJsonAsync<JsonObject>())!.Str("error")));
    }

    [Theory]
    [InlineData("/plan", "live")]
    [InlineData("/plan", "assisted")]
    [InlineData("/weather", "demo")]
    public async Task PaidServiceRequestsAreRejectedInDefaultTestMode(string path, string mode)
    {
        var profile=TestSupport.Profile(); profile["mode"]=mode;
        var response=await fixture.Client.PostAsJsonAsync(path,profile);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,response.StatusCode);
        Assert.Contains("禁止付费",(await response.Content.ReadFromJsonAsync<JsonObject>())!.Str("error"));
        var health=await fixture.Client.GetFromJsonAsync<JsonObject>("/health");
        Assert.False(health!.Bool("paidServicesAllowed"));
    }

    [Fact]
    public async Task DraftDoesNotSaveUntilConfirmedAndUpdatesRequireCurrentRevision()
    {
        var draft = await PlanAsync();
        Assert.False(draft.ContainsKey("id"));
        var saved = await SaveAsync(draft, 0);
        var id = saved.Str("id");
        try
        {
            Assert.Equal(1, saved.Int("revision"));
            Assert.Contains(await ListAsync(), trip => trip!.Str("id") == id);
            var stale = await fixture.Client.PostAsJsonAsync("/trips", new { trip = saved, expectedRevision = 0 });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            saved["title"] = "Confirmed update";
            var updated = await SaveAsync(saved, 1);
            Assert.Equal(2, updated.Int("revision"));
            Assert.Equal("Confirmed update", updated.Str("title"));
            Assert.Equal(id, updated.Str("id"));
        }
        finally { await fixture.Client.DeleteAsync("/trips/" + id); }
        Assert.DoesNotContain(await ListAsync(), trip => trip!.Str("id") == id);
    }

    [Fact]
    public async Task ConcurrentUpdatesAcceptExactlyOneWriter()
    {
        var saved = await SaveAsync(await PlanAsync(), 0);
        var id = saved.Str("id");
        try
        {
            var left = (JsonObject)saved.DeepClone(); left["title"] = "left";
            var right = (JsonObject)saved.DeepClone(); right["title"] = "right";
            var responses = await Task.WhenAll(
                fixture.Client.PostAsJsonAsync("/trips", new { trip = left, expectedRevision = 1 }),
                fixture.Client.PostAsJsonAsync("/trips", new { trip = right, expectedRevision = 1 }));
            Assert.Equal(1, responses.Count(response => response.IsSuccessStatusCode));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            var persisted = Assert.Single((await ListAsync()).OfType<JsonObject>(), trip => trip.Str("id") == id);
            Assert.Equal(2, persisted.Int("revision"));
            Assert.Contains(persisted.Str("title"), new[] { "left", "right" });
        }
        finally { await fixture.Client.DeleteAsync("/trips/" + id); }
    }

    [Fact]
    public async Task NewApiHostRetainsPreviouslySavedPostgresData()
    {
        var saved = await SaveAsync(await PlanAsync(), 0);
        var id = saved.Str("id");
        try
        {
            await using var second = fixture.CreateFactory();
            using var client = second.CreateClient();
            var records = await client.GetFromJsonAsync<JsonArray>("/trips");
            Assert.Contains(records!, trip => trip!.Str("id") == id && trip.Int("revision") == 1);
        }
        finally { await fixture.Client.DeleteAsync("/trips/" + id); }
    }

    [Fact]
    public async Task MemoryRequiresConsentAndCanBeRevoked()
    {
        var refusal = await fixture.Client.PostAsJsonAsync("/memories", new { consent = false, summary = "do not retain" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refusal.StatusCode);
        var response = await fixture.Client.PostAsJsonAsync("/memories", new
        {
            consent = true, summary = "quiet art galleries", profile = new { interests = new[] { "艺术" }, maxSteps = 5000 }
        });
        response.EnsureSuccessStatusCode();
        var memory = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = memory.Str("id");
        try
        {
            Assert.True(memory.Bool("consent"));
            Assert.Equal("local_only", memory.Str("scope"));
            Assert.Contains((await fixture.Client.GetFromJsonAsync<JsonArray>("/memories"))!, item => item!.Str("id") == id);
        }
        finally { (await fixture.Client.DeleteAsync("/memories/" + id)).EnsureSuccessStatusCode(); }
        Assert.DoesNotContain((await fixture.Client.GetFromJsonAsync<JsonArray>("/memories"))!, item => item!.Str("id") == id);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task MalformedRequestIs400RatherThanServerFailure(string content)
    {
        var response = await fixture.Client.PostAsync("/plan", new StringContent(content, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await response.Content.ReadFromJsonAsync<JsonObject>())!.Str("error")));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("\"unsupported\"")]
    public async Task InvalidModeCannotFallBackToDemo(string mode)
    {
        var profile = TestSupport.Profile(); profile["mode"] = JsonNode.Parse(mode);
        var response = await fixture.Client.PostAsJsonAsync("/plan", profile);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    public async Task InvalidTripIdCannotCreateAnUnrelatedTrip(string id)
    {
        var response = await fixture.Client.PostAsJsonAsync("/trips", new JsonObject
        {
            ["trip"] = new JsonObject { ["id"] = JsonNode.Parse(id), ["title"] = "invalid id fixture" },
            ["expectedRevision"] = 0
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MalformedReplacementDoesNotOverwriteSavedTrip()
    {
        var saved = await SaveAsync(await PlanAsync(), 0);
        var id = saved.Str("id");
        try
        {
            var malformed = (JsonObject)saved.DeepClone();
            malformed["days"] = new JsonArray();
            var response = await fixture.Client.PostAsJsonAsync("/trips", new { trip = malformed, expectedRevision = 1 });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var persisted = Assert.Single((await ListAsync()).OfType<JsonObject>(), trip => trip.Str("id") == id);
            Assert.True(JsonNode.DeepEquals(saved, persisted));
        }
        finally { await fixture.Client.DeleteAsync("/trips/" + id); }
    }

    [Fact]
    public async Task CrossOriginMutationIsRejectedBeforePlanning()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/plan")
        {
            Content = JsonContent.Create(TestSupport.Profile())
        };
        request.Headers.Add("Origin", "https://untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task LocalSetupRejectsMissingCsrfToken()
    {
        var response = await fixture.Client.PostAsync("/setup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["NVIDIA_API_KEY"] = "test-not-written"
        }));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(fixture.TemporaryRoot, ".secrets.json")));
    }

    private async Task<JsonObject> PlanAsync()
    {
        var profile = TestSupport.Profile(); profile["mode"] = "demo";
        var response = await fixture.Client.PostAsJsonAsync("/plan", profile);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private async Task<JsonObject> SaveAsync(JsonObject trip, int revision)
    {
        var response = await fixture.Client.PostAsJsonAsync("/trips", new { trip, expectedRevision = revision });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private async Task<JsonArray> ListAsync() => (await fixture.Client.GetFromJsonAsync<JsonArray>("/trips"))!;
}

/// <summary>为测试创建并清理专属 schema；real database fixtures isolate all writes from user data.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly string _schema = "sydney_tests_" + Guid.NewGuid().ToString("N");
    private readonly string _baseConnection = Environment.GetEnvironmentVariable("ConnectionStrings__SydneyBuddy")
        ?? "Host=127.0.0.1;Port=55432;Database=sydneybuddy;Username=sydneybuddy;Password=sydneybuddy_dev";
    private string _connection = "";
    private BuddyApiFactory? _factory;
    public string TemporaryRoot { get; } = Path.Combine(Path.GetTempPath(), "sydney-api-" + Guid.NewGuid().ToString("N"));
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(TemporaryRoot, "server"));
        File.Copy(Path.Combine(TestSupport.RepositoryRoot(), "server", "places.json"), Path.Combine(TemporaryRoot, "server", "places.json"));
        await using var connection = new NpgsqlConnection(_baseConnection);
        // 数据库不可用会明确失败；missing PostgreSQL is never silently skipped or replaced.
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA {_schema}", connection);
        await command.ExecuteNonQueryAsync();
        var settings = new NpgsqlConnectionStringBuilder(_baseConnection) { SearchPath = _schema, MaxPoolSize = 5 };
        _connection = settings.ConnectionString;
        _factory = CreateFactory();
        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") });
    }

    internal BuddyApiFactory CreateFactory() => new(_connection, new ProjectPaths(TemporaryRoot));

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        if (_connection.Length > 0)
        {
            await using var connection = new NpgsqlConnection(_baseConnection);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {_schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
        if (Directory.Exists(TemporaryRoot)) Directory.Delete(TemporaryRoot, recursive: true);
    }
}

/// <summary>托管真实应用并替换付费服务；keeps PostgreSQL while removing external network dependencies.</summary>
internal sealed class BuddyApiFactory(string connection, ProjectPaths paths) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(TestSupport.RepositoryRoot());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SydneyBuddy"] = connection,
            ["AllowPaidServices"] = "false",
            ["SYDNEYBUDDY_ROOT"] = paths.RepoRoot
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ProjectPaths>(); services.AddSingleton(paths);
            services.RemoveAll<IMemoryService>(); services.AddSingleton<IMemoryService>(new FakeMemory());
            services.RemoveAll<IExternalJsonClient>(); services.AddSingleton<IExternalJsonClient>(new FakeExternal());
        });
    }
}
