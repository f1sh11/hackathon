using System.Text.Json.Nodes;
using System.Text.Json;

namespace SydneyBuddy.Api.Tests;

/// <summary>验证完整行程约束与模型边界；planning tests retain the original mobile behavior.</summary>
public sealed class PlannerTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), "sydney-planner-" + Guid.NewGuid().ToString("N"));
    private readonly ProjectPaths _paths;
    private readonly FakeExternal _external = new();
    private readonly CatalogCache _cache = new();

    public PlannerTests()
    {
        Directory.CreateDirectory(Path.Combine(_temporaryRoot, "server"));
        File.Copy(Path.Combine(TestSupport.RepositoryRoot(), "server", "places.json"), Path.Combine(_temporaryRoot, "server", "places.json"));
        _paths = new ProjectPaths(_temporaryRoot);
    }

    private Planner Create() => new(new PlaceService(_external, new PlaceCatalog(_cache, _paths)),
        new RecommendationService(_external, new FakeMemory()), new RouteScheduler(new RouteService(_external)),
        new WeatherService(_external), new SecretStore(_paths));

    [Fact]
    public async Task FeasibleTripAccountsForReturnAndInternalWalking()
    {
        var result = await Create().PlanAsync(TestSupport.Profile(), "demo");
        var day = result.Array("days")![0]!.AsObject();
        var stops = day.Array("stops")!;
        var legs = day.Array("legs")!;
        Assert.InRange(day.Int("estimatedSteps"), 1, 10000);
        Assert.InRange(day.Number("estimatedCost"), 0, 100);
        Assert.Equal(stops.Count + 1, legs.Count);
        Assert.True(day.Number("estimatedSteps") > legs.Sum(leg => leg!.Number("meters")) / 0.75);
        Assert.Equal("demo", result.Str("mode"));
        Assert.Empty(_external.Calls);
        Assert.All(stops, stop => Assert.False(stop!.Bool("hoursVerified")));
    }

    [Fact]
    public async Task GeneratedDraftPassesSaveBoundaryWithoutChangingItsContent()
    {
        var result = await Create().PlanAsync(TestSupport.Profile(), "demo");
        var original = result.DeepClone();
        TripContractValidator.Validate(result);
        Assert.True(JsonNode.DeepEquals(original, result));
    }

    [Fact]
    public void IncompleteTripCannotCrossTheSaveBoundary()
    {
        Assert.Throws<JsonException>(() => TripContractValidator.Validate(new JsonObject()));
    }

    [Fact]
    public void SharedPublicCatalogCacheDoesNotLeakCallerMutations()
    {
        var catalog = new PlaceCatalog(_cache, _paths);
        var first = catalog.Get();
        var expected = first[0].Str("name");
        first[0]["name"] = "tampered by caller";
        var laterRequest = new PlaceCatalog(_cache, _paths).Get();
        Assert.Equal(expected, laterRequest[0].Str("name"));
        Assert.False(ReferenceEquals(first[0], laterRequest[0]));
        Assert.Empty(_external.Calls);
    }

    [Fact]
    public async Task ZeroBudgetRetainsOnlyFreeStops()
    {
        var profile = TestSupport.Profile(); profile["budget"] = 0;
        var result = await Create().PlanAsync(profile, "demo");
        Assert.Equal(0, result.Array("days")![0]!.Number("estimatedCost"));
    }

    [Fact]
    public async Task InfeasibleDayFailsBeforeSavingAnything()
    {
        var profile = TestSupport.Profile(); profile["endMinute"] = 610;
        await Assert.ThrowsAsync<ServiceException>(() => Create().PlanAsync(profile, "demo"));
    }

    [Fact]
    public async Task RequiredStopCannotBeSilentlyDropped()
    {
        var profile = TestSupport.Profile(); profile["mustVisit"] = new JsonArray("opera");
        var result = await Create().PlanAsync(profile, "demo");
        Assert.Contains(result.Array("days")!.SelectMany(day => day!.Array("stops")!), stop => stop!.Str("id") == "opera");
    }

    [Fact]
    public async Task InfeasibleRequiredStopBudgetIsRejected()
    {
        var profile = TestSupport.Profile(); profile["budget"] = 0; profile["mustVisit"] = new JsonArray("mca");
        await Assert.ThrowsAsync<ServiceException>(() => Create().PlanAsync(profile, "demo"));
    }

    [Fact]
    public async Task RainScenarioIsLabelledAndFiltersOutdoorStops()
    {
        var profile = TestSupport.Profile(); profile["weatherOverride"] = "rain";
        var result = await Create().PlanAsync(profile, "demo");
        Assert.Contains("非实时", result.Object("weather")!.Str("source"));
        Assert.All(result.Array("days")!.SelectMany(day => day!.Array("stops")!), stop => Assert.True(stop!.Bool("indoor")));
    }

    [Fact]
    public async Task MultipleDaysNeverDuplicateAttractions()
    {
        var result = await Create().PlanAsync(TestSupport.Profile(2), "demo");
        var ids = result.Array("days")!.SelectMany(day => day!.Array("stops")!).Select(stop => stop!.Str("id")).ToArray();
        Assert.Equal(2, result.Array("days")!.Count);
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void InvalidCoordinatesAreRejectedBeforeCallingAProvider()
    {
        var profile = TestSupport.Profile(); profile["start"] = new JsonObject { ["lat"] = 0, ["lng"] = 0 };
        Assert.Throws<ServiceException>(() => PlanValidator.Validate(profile, Create().Catalog));
        Assert.Empty(_external.Calls);
    }

    [Fact]
    public async Task LiveModeCannotSilentlyFallBackWithoutConfiguration()
    {
        await Assert.ThrowsAsync<ServiceException>(() => Create().PlanAsync(TestSupport.Profile(), "live"));
        Assert.Empty(_external.Calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    public void InvalidModelJsonIsRejected(string content) => Assert.Throws<ServiceException>(() => Planner.ParseModelJson(content));

    [Fact]
    public void FencedModelJsonIsAcceptedWithoutTreatingFenceAsContent()
    {
        var result = Planner.ParseModelJson("```json\n{\"preferredIds\":[\"opera\"]}\n```");
        Assert.Equal("opera", Assert.Single(result.Array("preferredIds")!)!.GetValue<string>());
    }

    [Fact]
    public void SplitOpeningWindowsNeverMergeAcrossMiddayClosure()
    {
        var place = JsonNode.Parse("""{"periods":[{"open":{"day":6,"hour":9},"close":{"day":6,"hour":12}},{"open":{"day":6,"hour":14},"close":{"day":6,"hour":17}}]}""")!.AsObject();
        var windows = Planner.OpeningWindows(place, "2026-10-24", out var verified);
        Assert.True(verified);
        Assert.Equal(new[] { (540, 720), (840, 1020) }, windows);
    }

    [Fact]
    public async Task FatigueRequestLowersStepLimitAndDoesNotMutateCallerInput()
    {
        var profile = TestSupport.Profile(); profile["request"] = "今天累了，想少走一点";
        var result = await Create().PlanAsync(profile, "demo");
        Assert.Equal(7000, result.Object("profile")!.Int("maxSteps"));
        Assert.Equal(10000, profile.Int("maxSteps"));
        Assert.True(result.Array("days")![0]!.Int("estimatedSteps") <= 7000);
    }

    [Fact]
    public async Task MixedWeatherFiltersOnlyTheRainyDay()
    {
        await File.WriteAllTextAsync(Path.Combine(_temporaryRoot, ".secrets.json"), """{"GOOGLE_SERVER_KEY":"provider-test-key","NVIDIA_API_KEY":"model-test-key"}""");
        var planner = Create();
        _external.Respond = call =>
        {
            if (call.Url.Contains("places:searchText", StringComparison.Ordinal))
            {
                var place = planner.Catalog.Single(place => place.Str("query") == call.Payload!.Str("textQuery"));
                return new JsonObject { ["places"] = new JsonArray(new JsonObject
                {
                    ["id"] = "google-" + place.Str("id"),
                    ["location"] = new JsonObject { ["latitude"] = place.Number("lat"), ["longitude"] = place.Number("lng") }
                }) };
            }
            if (call.Url.Contains("weather.googleapis.com", StringComparison.Ordinal))
                return new JsonObject { ["forecastDays"] = new JsonArray(WeatherTests.Forecast(24, 80), WeatherTests.Forecast(25, 10)) };
            if (call.Url.Contains("chat/completions", StringComparison.Ordinal))
                return new JsonObject { ["choices"] = new JsonArray(new JsonObject
                {
                    ["message"] = new JsonObject { ["content"] = "{\"preferredIds\":[],\"explanation\":\"test ranking\"}" }
                }) };
            if (call.Url.Contains("computeRoutes", StringComparison.Ordinal))
                return new JsonObject { ["routes"] = new JsonArray(new JsonObject { ["duration"] = "120s", ["distanceMeters"] = 130 }) };
            throw new InvalidOperationException("Unexpected provider URL.");
        };
        var profile = TestSupport.Profile(2); profile["maxSteps"] = 18000; profile["interests"] = new JsonArray("风景");
        var result = await planner.PlanAsync(profile, "live");
        Assert.All(result.Array("days")![0]!.Array("stops")!, stop => Assert.True(stop!.Bool("indoor")));
        Assert.Contains(result.Array("days")![1]!.Array("stops")!, stop => !stop!.Bool("indoor"));
        Assert.Equal(70, Assert.Single(_external.Calls, call => call.Url.Contains("chat/completions", StringComparison.Ordinal)).TimeoutSeconds);
    }

    public void Dispose()
    {
        _cache.Dispose();
        Directory.Delete(_temporaryRoot, recursive: true);
    }
}
