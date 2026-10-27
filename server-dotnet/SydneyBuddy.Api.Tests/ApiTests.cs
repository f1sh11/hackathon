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
    public async Task MultiDayInlineAdjustmentPreservesUntouchedDayAndRejectsStaleSave() {
        var profile=TestSupport.Profile(3);profile["mode"]="demo";profile["budget"]=500;
        var planned=(await (await fixture.Client.PostAsJsonAsync("/plan",profile)).Content.ReadFromJsonAsync<JsonObject>())!;
        var saved=(await (await fixture.Client.PostAsJsonAsync("/trips",new{trip=planned,expectedRevision=0})).Content.ReadFromJsonAsync<JsonObject>())!;
        var proposed=(await (await fixture.Client.PostAsJsonAsync("/schedule/editor",new{trip=saved})).Content.ReadFromJsonAsync<JsonObject>())!;
        var body=new{trip=proposed,expectedRevision=saved.Int("revision"),adjustedDays=new[]{0,2}};
        var response=await fixture.Client.PostAsJsonAsync("/trips",body);response.EnsureSuccessStatusCode();
        var accepted=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(JsonNode.DeepEquals(saved.Array("days")![1],accepted.Array("days")![1]));
        Assert.True(JsonNode.DeepEquals(saved["profile"],accepted["profile"]));Assert.Equal(2,accepted.Int("revision"));
        Assert.Equal(HttpStatusCode.Conflict,(await fixture.Client.PostAsJsonAsync("/trips",body)).StatusCode);
        var invalid=await fixture.Client.PostAsJsonAsync("/trips",new{trip=accepted,expectedRevision=2,adjustedDays=Array.Empty<int>()});
        Assert.False(invalid.IsSuccessStatusCode);
        var undo=await fixture.Client.PostAsJsonAsync("/trips",new{trip=accepted,expectedRevision=2,undoAdjustment=true});undo.EnsureSuccessStatusCode();
        Assert.True(JsonNode.DeepEquals(saved["days"],(await undo.Content.ReadFromJsonAsync<JsonObject>())!["days"]));
    }
    [Fact]
    public async Task AppleWalkingTimesPersistOnlySelectedDayAndRejectStaleRevision() {
        var profile=TestSupport.Profile(3);profile["mode"]="demo";
        var plan=(await (await fixture.Client.PostAsJsonAsync("/plan",profile)).Content.ReadFromJsonAsync<JsonObject>())!;
        var saved=(await (await fixture.Client.PostAsJsonAsync("/trips",new{trip=plan,expectedRevision=0})).Content.ReadFromJsonAsync<JsonObject>())!;
        var legs=new JsonArray(saved.Array("days")![0]!.Array("legs")!.Select(_=>(JsonNode)new JsonObject{["meters"]=100,["seconds"]=60}).ToArray());
        var body=new{trip=new{id=saved.Str("id")},expectedRevision=saved.Int("revision"),walkingTimeDay=0,walkingLegs=legs};
        var response=await fixture.Client.PostAsJsonAsync("/trips",body);response.EnsureSuccessStatusCode();
        var accepted=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(JsonNode.DeepEquals(saved.Array("days")![1],accepted.Array("days")![1]));
        Assert.Equal(2,accepted.Int("revision"));
        Assert.Equal(HttpStatusCode.Conflict,(await fixture.Client.PostAsJsonAsync("/trips",body)).StatusCode);
        var reloaded=(await fixture.Client.GetFromJsonAsync<JsonArray>("/trips"))!.OfType<JsonObject>().Single(t=>t.Str("id")==saved.Str("id"));
        Assert.True(JsonNode.DeepEquals(accepted["days"],reloaded["days"]));
        var undo=await fixture.Client.PostAsJsonAsync("/trips",new{trip=new{id=saved.Str("id")},expectedRevision=2,undoAdjustment=true});undo.EnsureSuccessStatusCode();
        Assert.True(JsonNode.DeepEquals(saved["days"],(await undo.Content.ReadFromJsonAsync<JsonObject>())!["days"]));
    }
    [Fact]
    public async Task SingleDayAdjustmentPreservesOtherAcceptedDaysExactly() {
        var profile=TestSupport.Profile(3);profile["mode"]="demo";profile["budget"]=500;
        var planned=(await (await fixture.Client.PostAsJsonAsync("/plan",profile)).Content.ReadFromJsonAsync<JsonObject>())!;
        var saved=(await (await fixture.Client.PostAsJsonAsync("/trips",new{trip=planned,expectedRevision=0})).Content.ReadFromJsonAsync<JsonObject>())!;
        var proposed=(await (await fixture.Client.PostAsJsonAsync("/schedule/editor",new{trip=saved})).Content.ReadFromJsonAsync<JsonObject>())!;
        proposed.Object("profile")!.Clear();foreach(var pair in saved.Object("profile")!)proposed.Object("profile")![pair.Key]=pair.Value?.DeepClone();
        proposed.Object("profile")!["budget"]=9999; // Client profile changes must not affect a single-day adjustment.
        var response=await fixture.Client.PostAsJsonAsync("/trips",new{trip=proposed,expectedRevision=saved.Int("revision"),adjustedDay=0});
        response.EnsureSuccessStatusCode();var accepted=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(JsonNode.DeepEquals(saved.Array("days")![1],accepted.Array("days")![1]));Assert.True(JsonNode.DeepEquals(saved.Array("days")![2],accepted.Array("days")![2]));
        Assert.Equal(2,accepted.Int("revision"));Assert.True(JsonNode.DeepEquals(saved["profile"],accepted["profile"]));
        var undo=await fixture.Client.PostAsJsonAsync("/trips",new{trip=accepted,expectedRevision=2,undoAdjustment=true});undo.EnsureSuccessStatusCode();var restored=(await undo.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(JsonNode.DeepEquals(saved["days"],restored["days"]));Assert.Equal(3,restored.Int("revision"));Assert.Null(restored["previousAdjustment"]);
        var nameDraft=(JsonObject)restored.DeepClone();nameDraft["title"]="海风假期";
        var rename=await fixture.Client.PostAsJsonAsync("/trips",new{trip=nameDraft,expectedRevision=3,renameOnly=true});rename.EnsureSuccessStatusCode();var renamed=(await rename.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.True(JsonNode.DeepEquals(restored["days"],renamed["days"]));Assert.Equal("海风假期",renamed.Str("title"));
    }
    [Fact]
    public async Task ScheduleEditorSavesAndReloadsRecomputedSnapshotAndRejectsConflicts()
    {
        var profile=TestSupport.Profile(3);profile["mode"]="demo";profile["budget"]=500;
        var planned=await (await fixture.Client.PostAsJsonAsync("/plan",profile)).Content.ReadFromJsonAsync<JsonObject>();
        var response=await fixture.Client.PostAsJsonAsync("/schedule/editor",new JsonObject{["trip"]=planned!.DeepClone()});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var trip=(await response.Content.ReadFromJsonAsync<JsonObject>())!;
        var schedule=trip.Object("schedule")!;
        Assert.Equal(3,schedule.Array("days")!.Count);
        var savedResponse=await fixture.Client.PostAsJsonAsync("/trips",new JsonObject{["trip"]=trip.DeepClone(),["expectedRevision"]=0});
        Assert.Equal(HttpStatusCode.OK,savedResponse.StatusCode);
        var saved=(await savedResponse.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.NotNull(saved.Object("schedule"));
        var reloaded=(await fixture.Client.GetFromJsonAsync<JsonArray>("/trips"))!.OfType<JsonObject>().First(x=>x.Str("id")==saved.Str("id"));
        Assert.Equal(saved.Object("schedule")!.Number("totalCost"),reloaded.Object("schedule")!.Number("totalCost"));
        var activity=trip.Object("schedule")!.Array("days")![0]!.Array("activities")!.OfType<JsonObject>().First(a=>a.Str("kind")=="attraction");
        activity["fixedStart"]=1430;
        var rejected=await fixture.Client.PostAsJsonAsync("/trips",new JsonObject{["trip"]=trip,["expectedRevision"]=0});
        Assert.Equal(HttpStatusCode.UnprocessableEntity,rejected.StatusCode);
    }

    [Fact]
    public async Task JourneyEndpointIsAvailableAndUnconfiguredTransitIsExplicit()
    {
        var response=await fixture.Client.PostAsJsonAsync("/journey/plan",JsonNode.Parse("""{"departureTime":"2026-10-23T10:00:00+11:00","modes":["transit"],"points":[{"name":"Start","lat":-33.86,"lng":151.21},{"name":"End","lat":-33.85,"lng":151.22}]}"""));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,response.StatusCode);
        Assert.Contains("尚未启用",(await response.Content.ReadFromJsonAsync<JsonObject>())!.Str("error"));
        Assert.False((await fixture.Client.GetFromJsonAsync<JsonObject>("/health"))!.Bool("transitConfigured"));
    }
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
        Assert.True(mobile!.Count>=14);
        Assert.Contains(mobile.OfType<JsonObject>(),p=>p.Str("activityType")=="meal"&&!string.IsNullOrEmpty(p.Str("sourceURL")));
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
    public async Task NavigationEndpointFailsClearlyWhenRouteServiceIsNotConfigured()
    {
        var response = await fixture.Client.PostAsJsonAsync("/navigation/route", new { locations = new[] { new { lat = -33.86, lon = 151.21 }, new { lat = -33.85, lon = 151.22 } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Valhalla", (await response.Content.ReadFromJsonAsync<JsonObject>())!.Str("error"));
        var health = await fixture.Client.GetFromJsonAsync<JsonObject>("/health");
        Assert.False(health!.Bool("navigationConfigured"));
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

    [Fact]
    public async Task FailedWorldCheckRollsBackAndSameRequestCanRetry() {
        var saved=await SaveAsync(await PlanAsync(),0);var id=saved.Str("id");
        try {
            await using var host=fixture.CreateFactory();using var client=host.CreateClient();
            using(var scope=host.Services.CreateScope()) {
                var repository=scope.ServiceProvider.GetRequiredService<TripRepository>();
                var invalid=(JsonObject)saved.DeepClone();invalid["days"]=new JsonArray();await repository.SaveAsync(invalid,1);
            }
            var request=new{requestId="rollback_"+Guid.NewGuid().ToString("N")};
            Assert.False((await client.PostAsJsonAsync("/trips/"+id+"/simulation-runs",request)).IsSuccessStatusCode);
            Assert.False((await client.GetFromJsonAsync<JsonObject>("/trips/"+id+"/world"))!.Bool("available"));
            using(var scope=host.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TripRepository>().SaveAsync(saved,2);
            var retry=await client.PostAsJsonAsync("/trips/"+id+"/simulation-runs",request);retry.EnsureSuccessStatusCode();
            Assert.Equal("completed",(await retry.Content.ReadFromJsonAsync<JsonObject>())!.Str("status"));
        } finally {await fixture.Client.DeleteAsync("/trips/"+id);}
    }

    [Fact]
    public async Task WorldChecksSerializeRetriesAndRejectDecisionsAfterRevisionChanges() {
        var saved=await SaveAsync(await PlanAsync(),0);var id=saved.Str("id");
        try {
            var request="concurrent_"+Guid.NewGuid().ToString("N");
            var responses=await Task.WhenAll(Enumerable.Range(0,3).Select(_=>fixture.Client.PostAsJsonAsync("/trips/"+id+"/simulation-runs",new{requestId=request})));
            var runs=new List<JsonObject>();foreach(var response in responses){response.EnsureSuccessStatusCode();runs.Add((await response.Content.ReadFromJsonAsync<JsonObject>())!);}
            Assert.Single(runs.Select(x=>x.Str("id")).Distinct());
            var run=runs[0];Assert.False(run.Bool("stale"));Assert.Null(run.Object("summary")!["after"]);
            var suggestion=run.Array("suggestions")!.OfType<JsonObject>().First();
            var path="/simulation-runs/"+run.Str("id")+"/decisions";
            var decision=new{suggestionId=suggestion.Str("id"),decision="rejected"};
            foreach(var _ in Enumerable.Range(0,2))(await fixture.Client.PostAsJsonAsync(path,decision)).EnsureSuccessStatusCode();
            Assert.Single((await fixture.Client.GetFromJsonAsync<JsonObject>("/simulation-runs/"+run.Str("id")))!.Array("decisions")!);
            Assert.False((await fixture.Client.PostAsJsonAsync(path,new{suggestionId=suggestion.Str("id"),decision="accepted"})).IsSuccessStatusCode);
            var recheck=await fixture.Client.PostAsJsonAsync("/trips/"+id+"/simulation-runs",new{requestId="recheck_"+Guid.NewGuid().ToString("N")});recheck.EnsureSuccessStatusCode();
            var checkedAgain=(await recheck.Content.ReadFromJsonAsync<JsonObject>())!;
            Assert.Equal("rejected",Assert.Single(checkedAgain.Array("decisions")!.OfType<JsonObject>()).Str("decision"));
            saved["title"]="更新过的行程";
            (await fixture.Client.PostAsJsonAsync("/trips",new{trip=saved,expectedRevision=saved.Int("revision"),renameOnly=true})).EnsureSuccessStatusCode();
            var latest=await fixture.Client.GetFromJsonAsync<JsonObject>("/trips/"+id+"/world");Assert.True(latest!.Object("run")!.Bool("stale"));
            Assert.False((await fixture.Client.PostAsJsonAsync(path,decision)).IsSuccessStatusCode);
        } finally {await fixture.Client.DeleteAsync("/trips/"+id);}
    }

    [Fact]
    public async Task WorldModelPersistsSimulationSnapshotsAndUserDecisionWithoutChangingTrip()
    {
        var saved = await SaveAsync(await PlanAsync(), 0);
        var id = saved.Str("id");
        try
        {
            var empty = await fixture.Client.GetFromJsonAsync<JsonObject>("/trips/" + id + "/world");
            Assert.False(empty!.Bool("available"));

            var request = "simulation_" + Guid.NewGuid().ToString("N");
            var response = await fixture.Client.PostAsJsonAsync("/trips/" + id + "/simulation-runs", new { requestId = request });
            response.EnsureSuccessStatusCode();
            var run = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
            Assert.Equal("completed", run.Str("status"));
            Assert.Equal(id, run.Str("tripId"));
            Assert.NotEmpty(run.Array("snapshots")!);
            Assert.NotEmpty(run.Array("suggestions")!);
            Assert.Equal(saved.Int("revision"), run.Int("tripRevision"));

            var retry = await fixture.Client.PostAsJsonAsync("/trips/" + id + "/simulation-runs", new { requestId = request });
            var sameRun = (await retry.Content.ReadFromJsonAsync<JsonObject>())!;
            Assert.Equal(run.Str("id"), sameRun.Str("id"));

            var suggestion = run.Array("suggestions")![0]!.AsObject().Str("id");
            var decision = await fixture.Client.PostAsJsonAsync("/simulation-runs/" + run.Str("id") + "/decisions",
                new { suggestionId = suggestion, decision = "rejected" });
            decision.EnsureSuccessStatusCode();
            var decided = (await decision.Content.ReadFromJsonAsync<JsonObject>())!;
            var eventData = Assert.Single(decided.Array("decisions")!.OfType<JsonObject>());
            Assert.Equal("rejected", eventData.Str("decision"));
            Assert.True(eventData.Bool("preserveUserChoice"));

            var latest = await fixture.Client.GetFromJsonAsync<JsonObject>("/trips/" + id + "/world");
            Assert.True(latest!.Bool("available"));
            Assert.Equal(run.Str("id"), latest.Object("run")!.Str("id"));
            var unchanged = Assert.Single((await ListAsync()).OfType<JsonObject>(), trip => trip.Str("id") == id);
            Assert.Equal(saved.Int("revision"), unchanged.Int("revision"));
        }
        finally { await fixture.Client.DeleteAsync("/trips/" + id); }
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

    [Fact]
    public async Task PlanningRunLinksPreparePlanAndFailedRequestsWithoutLoggingPrivateText() {
        var id=Guid.NewGuid().ToString();var raw=TestSupport.Profile();raw["mode"]="demo";
        foreach(var path in new[]{"/plan/prepare","/plan"}) {
            using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(raw)};request.Headers.Add(PlanningRunJournal.Header,id);
            var response=await fixture.Client.SendAsync(request);response.EnsureSuccessStatusCode();Assert.Equal(id,response.Headers.GetValues(PlanningRunJournal.Header).Single());
        }
        using(var request=new HttpRequestMessage(HttpMethod.Post,"/plan"){Content=new StringContent("{",Encoding.UTF8,"application/json")}) {
            request.Headers.Add(PlanningRunJournal.Header,id);var response=await fixture.Client.SendAsync(request);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);var error=(await response.Content.ReadFromJsonAsync<JsonObject>())!;Assert.Equal(id,error.Str("runId"));
        }
        var run=(await fixture.Client.GetFromJsonAsync<JsonObject>("/planning/runs/"+id))!;var rows=run.Array("events")!.OfType<JsonObject>().ToArray();
        Assert.All(rows,x=>Assert.Equal(id,x.Str("run_id")));Assert.Contains(rows,x=>x.Str("action")=="request_failed");Assert.Equal(Enumerable.Range(1,rows.Length),rows.Select(x=>x.Int("sequence")));
        Assert.DoesNotContain("NVIDIA_API_KEY",run.ToJsonString());Assert.DoesNotContain("Authorization",run.ToJsonString());
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
            ["Navigation:ValhallaUrl"] = "",
            ["Transit:OtpGraphqlUrl"] = "",
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
