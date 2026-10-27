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

    private static JsonObject SearchPlace(string id,string name,double lat) => new() {
        ["id"]="apple:"+id,["name"]=name,["lat"]=lat,["lng"]=151.2126,["source"]="Apple Maps",["category"]="风景",["poiKind"]="pointOfInterest"
    };
    private static JsonObject ConflictingPlace(string id,string name) {
        var place=SearchPlace("unused",name,-33.862);place["id"]=id;place["source"]="Overture Maps";return place;
    }
    [Theory]
    [InlineData("overture:6807df81-5509-4b67-8318-8c619ef155eb","Great Barrier Reef Australia")]
    [InlineData("overture:ebf9a059-1f0f-420d-9a95-dea2a5da6424","Ayers Rock, Uluru NT")]
    public async Task ConfirmedLocationConflictsCannotEnterPlanOrWalkingBackups(string id,string name) {
        var profile=TestSupport.Profile();profile["interests"]=new JsonArray("风景");profile["maxSteps"]=60000;
        profile["planningCandidates"]=new JsonArray(ConflictingPlace(id,name),SearchPlace("safe","Harbour Garden",-33.862));
        var plan=await Create().PlanAsync(profile,"demo");
        Assert.DoesNotContain(plan.Array("days")!.OfType<JsonObject>().SelectMany(x=>x.Array("stops")!.OfType<JsonObject>()),s=>s.Str("id")==id);
        var shortlist=await Create().PrepareWalkingAsync(profile);
        Assert.DoesNotContain(shortlist.Array("candidateDays")!.OfType<JsonArray>().SelectMany(x=>x),p=>p!.GetValue<string>()==id);
        Assert.Contains(plan.Array("trace")!.OfType<JsonObject>(),x=>x.Str("title")=="核对地点位置");
        profile["mustVisit"]=new JsonArray(id);
        var error=await Assert.ThrowsAsync<ServiceException>(()=>Create().PlanAsync(profile,"demo"));
        Assert.Contains("坐标存在冲突",error.Message);Assert.Contains(id,profile.Array("mustVisit")!.ToJsonString());
    }
    [Fact] public void SimilarNamesAndCorrectedSourceCoordinatesAreNotBlacklisted() {
        Assert.False(PlanningLocationGuard.HasKnownConflict(SearchPlace("cafe","Uluru Cafe",-33.86)));
        var corrected=ConflictingPlace("overture:ebf9a059-1f0f-420d-9a95-dea2a5da6424","Ayers Rock, Uluru NT");
        corrected["lat"]=-25.344;corrected["lng"]=131.037;
        Assert.False(PlanningLocationGuard.HasKnownConflict(corrected));
        var profile=TestSupport.Profile();profile["mustVisit"]=new JsonArray();
        Assert.Throws<ServiceException>(()=>TravelConstraints.Apply(profile,[ConflictingPlace("overture:ebf9a059-1f0f-420d-9a95-dea2a5da6424","Ayers Rock") ]));
    }

    [Theory]
    [InlineData("Le Jolly Christmas Market",true)]
    [InlineData("聖誕市集",true)]
    [InlineData("圣诞集市",true)]
    [InlineData("Sydney Royal Easter Show",true)]
    [InlineData("Royal Botanic Garden",false)]
    [InlineData("Christmas Shop",false)]
    public void SeasonalNamesRequireDatesWithoutAssumingAClosingDate(string name,bool expected) =>
        Assert.Equal(expected,SeasonalEventGuard.NeedsVerifiedDates(SearchPlace("test",name,-33.86)));

    [Theory]
    [InlineData("2026-10-29")]
    [InlineData("2026-12-20")]
    public async Task SearchEventCannotEnterPlanOrWalkingBackupsEvenInAnAssumedSeason(string date) {
        var profile=TestSupport.Profile();profile["date"]=date;profile["interests"]=new JsonArray("风景");profile["maxSteps"]=60000;
        profile["planningCandidates"]=new JsonArray(SearchPlace("seasonal","Le Jolly Christmas Market",-33.86),SearchPlace("garden","Harbour Garden",-33.862));
        var plan=await Create().PlanAsync(profile,"demo");
        Assert.DoesNotContain(plan.Array("days")!.OfType<JsonObject>().SelectMany(x=>x.Array("stops")!.OfType<JsonObject>()),s=>s.Str("id")=="apple:seasonal");
        var shortlist=await Create().PrepareWalkingAsync(profile);
        Assert.DoesNotContain(shortlist.Array("candidateDays")!.OfType<JsonArray>().SelectMany(x=>x),id=>id!.GetValue<string>()=="apple:seasonal");
        Assert.Contains(plan.Array("trace")!.OfType<JsonObject>(),x=>x.Str("title")=="核对限时活动");
    }
    [Fact] public async Task RequiredUnknownEventExplainsTheBlockInsteadOfSilentlyDroppingIt() {
        var profile=TestSupport.Profile();profile["mustDo"]="Le Jolly Christmas Market";
        profile["planningCandidates"]=new JsonArray(SearchPlace("seasonal","Le Jolly Christmas Market",-33.86));
        var error=await Assert.ThrowsAsync<ServiceException>(()=>Create().PlanAsync(profile,"demo"));
        Assert.Contains("尚未核实",error.Message);Assert.Equal("Le Jolly Christmas Market",profile.Str("mustDo"));
    }
    [Fact] public void PermanentRestaurantIsNotRemovedBecauseOfItsFestiveName() {
        var cafe=SearchPlace("cafe","Christmas Market Cafe",-33.86);cafe["activityType"]="meal";
        Assert.False(SeasonalEventGuard.NeedsVerifiedDates(cafe));
    }

    [Fact]
    public async Task ExplicitAvoidAndMustVisitConstrainActualStops()
    {
        var profile=TestSupport.Profile(3);profile["avoid"]="不逛博物馆";profile["mustDo"]="悉尼歌剧院";
        var trip=await Create().PlanAsync(profile,"demo");
        var stops=trip.Array("days")!.OfType<JsonObject>().SelectMany(d=>d.Array("stops")!.OfType<JsonObject>()).ToArray();
        Assert.DoesNotContain(stops,p=>p.Str("name").Contains("博物馆"));Assert.Contains(stops,p=>p.Str("id")=="opera");
    }
    [Fact]
    public async Task UnknownOrConflictingRequirementsCannotSilentlyGenerate()
    {
        var profile=TestSupport.Profile();profile["mustDo"]="月球基地";
        await Assert.ThrowsAsync<ServiceException>(()=>Create().PlanAsync(profile,"demo"));
        profile["mustDo"]="悉尼歌剧院";profile["avoid"]="歌剧院";
        await Assert.ThrowsAsync<ServiceException>(()=>Create().PlanAsync(profile,"demo"));
        profile["mustDo"]="无";profile["avoid"]="轮椅无障碍";
        await Assert.ThrowsAsync<ServiceException>(()=>Create().PlanAsync(profile,"demo"));
    }
    [Fact]
    public async Task AppleTimeUpdatePreservesOtherDaysAndRejectsLimitOverflow()
    {
        var trip=await Create().PlanAsync(TestSupport.Profile(3),"demo");var old=trip.ToJsonString();
        var day=trip.Array("days")![0]!.AsObject();
        JsonArray Legs(double seconds)=>new(day.Array("legs")!.Select(_=>(JsonNode)new JsonObject{["meters"]=10,["seconds"]=seconds}).ToArray());
        var catalog=Create().Catalog;var updated=WalkingTimeUpdate.Apply(trip,0,Legs(60),catalog);
        Assert.Equal(old,trip.ToJsonString());Assert.Equal(trip.Array("days")![1]!.ToJsonString(),updated.Array("days")![1]!.ToJsonString());
        Assert.All(updated.Array("days")![0]!.Array("legs")!.OfType<JsonObject>(),l=>Assert.Contains("Apple",l.Str("source")));
        Assert.Throws<ServiceException>(()=>WalkingTimeUpdate.Apply(trip,0,Legs(86400),catalog));
        Assert.Throws<ServiceException>(()=>WalkingTimeUpdate.Apply(trip,0,new JsonArray(),catalog));
    }
    [Fact]
    public async Task LowEnergyAndRelaxedRhythmAllowUsefulThirdStop()
    {
        var profile=TestSupport.Profile();profile["energy"]="低精力";profile["rhythm"]="轻松随意";
        var result=await Create().PlanAsync(profile,"demo");
        Assert.InRange(result.Array("days")![0]!.Array("stops")!.Count,1,3);
    }
    [Fact]
    public async Task FifteenDayTripCanBeGeneratedAndSaved()
    {
        var result=await Create().PlanAsync(TestSupport.Profile(15), "demo");
        Assert.Equal(15,result.Array("days")!.Count);
        Assert.Contains(result.Array("days")!.OfType<JsonObject>(),d=>d.Bool("freeTime") && d.Array("stops")!.Count==0);
        TripContractValidator.Validate(result);
    }

    [Fact]
    public async Task SevenDaysWithLimitedBudgetKeepsFeasibleDaysAndExplainsFreeTime()
    {
        var profile = TestSupport.Profile(7);
        profile["budget"] = 200d / 7;
        profile["interests"] = new JsonArray("艺术");
        var result = await Create().PlanAsync(profile, "demo");
        var days = result.Array("days")!.OfType<JsonObject>().ToArray();
        Assert.Equal(7, days.Length);
        Assert.Contains(days,day=>day.Array("stops")!.Count<3);
        Assert.All(days, day => {
            Assert.InRange(day.Number("estimatedCost"), 0, 200d / 7);
            Assert.InRange(day.Int("estimatedSteps"), 0, 10000);
            if (day.Bool("freeTime")) Assert.NotEmpty(day.Array("warnings")!);
        });
        TripContractValidator.Validate(result);
    }

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
        var profile = TestSupport.Profile(); profile["weatherOverride"] = "rain";profile["weatherTolerance"]="avoidRain";
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
        Assert.Equal(8000, result.Object("profile")!.Int("maxSteps"));
        Assert.Equal(10000, profile.Int("maxSteps"));
        Assert.True(result.Array("days")![0]!.Int("estimatedSteps") <= 8000);
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
        Assert.All(result.Array("days")![0]!.Array("stops")!.OfType<JsonObject>().Where(s=>!s.Bool("indoor")), stop => {Assert.True(stop.Int("visitMinutes")<=30);Assert.Contains("预报有雨",stop.Str("note"));});
        Assert.Contains(result.Array("days")![1]!.Array("stops")!, stop => !stop!.Bool("indoor"));
        Assert.Equal(70, Assert.Single(_external.Calls, call => call.Url.Contains("chat/completions", StringComparison.Ordinal)).TimeoutSeconds);
    }

    public void Dispose()
    {
        _cache.Dispose();
        Directory.Delete(_temporaryRoot, recursive: true);
    }
}
