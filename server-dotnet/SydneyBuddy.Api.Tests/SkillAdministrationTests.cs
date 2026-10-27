using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace SydneyBuddy.Api.Tests;

public sealed class SkillAdministrationTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "skill-admin-" + Guid.NewGuid());
    SkillAdministration Store() => new(new ProjectPaths(root, root), new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["SKILL_ADMIN_TOKEN"] = new string('a', 40) }).Build());
    static JsonObject Bundle(string version, string en = "New rule.", string zh = "新增規則。") => new() {
        ["version"] = version, ["baseVersion"] = ItinerarySkill.BaseVersion, ["enAppendix"] = en, ["zhHantAppendix"] = zh };
    [Fact] public void IndependentAdminCredentialIsRequiredAndDisabledByDefault()
    {
        var store = Store(); Assert.True(store.Authorized(new string('a', 40))); Assert.False(store.Authorized(new string('b', 40))); Assert.False(store.Authorized(""));
        var disabled = new SkillAdministration(new(root, root), new ConfigurationBuilder().Build()); Assert.False(disabled.Authorized(new string('a', 40)));
    }
    [Fact] public async Task UploadDoesNotActivateAndVersionsCannotBeOverwritten()
    {
        var s = Store(); var result = await s.UploadAsync(Bundle("test.1")); Assert.False(result.Bool("activated"));
        Assert.Equal(ItinerarySkill.BaseVersion, (await s.StatusAsync()).Str("activeVersion"));
        await s.UploadAsync(Bundle("test.1"));
        await Assert.ThrowsAsync<RevisionConflictException>(() => s.UploadAsync(Bundle("test.1", "Different rule.")));
    }
    [Fact] public async Task ActivationRetainsBaseRulesAndRejectsStaleWritersOrRemovedAppendices()
    {
        var s = Store(); var fingerprint = (await s.StatusAsync()).Str("activeFingerprint"); await s.UploadAsync(Bundle("test.1"));
        await s.ActivateAsync("test.1", fingerprint, false, false);
        var active = ItinerarySkill.Load(s.ActivePath); Assert.StartsWith(ItinerarySkill.Load(null).English, active.English);
        await s.UploadAsync(Bundle("test.2", "New rule. More.", "新增規則。更多。"));
        await Assert.ThrowsAsync<RevisionConflictException>(() => s.ActivateAsync("test.2", fingerprint, false, false));
        var current = (await s.StatusAsync()).Str("activeFingerprint");
        await s.ActivateAsync("test.2", current, false, false);
        current = (await s.StatusAsync()).Str("activeFingerprint");
        await Assert.ThrowsAsync<ServiceException>(() => s.ActivateAsync("test.1", current, false, false));
        Assert.Equal("test.2", ItinerarySkill.Load(s.ActivePath).Version);
    }
    [Fact] public async Task RollbackPreviewsRemovedRulesRequiresConsentAndCanRecoverCorruption()
    {
        var s = Store(); await s.UploadAsync(Bundle("test.1")); await s.ActivateAsync("test.1", (await s.StatusAsync()).Str("activeFingerprint"), false, false);
        var preview = await s.GetAsync(ItinerarySkill.BaseVersion); Assert.Equal("New rule.", preview.Object("removedRules")!.Str("enAppendix"));
        var current = preview.Str("activeFingerprint");
        await Assert.ThrowsAsync<ServiceException>(() => s.ActivateAsync(ItinerarySkill.BaseVersion, current, true, false));
        File.WriteAllText(s.ActivePath, "{"); var invalid = await s.StatusAsync(); Assert.False(invalid.Bool("activeValid"));
        await s.ActivateAsync(ItinerarySkill.BaseVersion, invalid.Str("activeFingerprint"), true, true);
        Assert.Equal("embedded", ItinerarySkill.Load(s.ActivePath).Source);
    }
    [Fact] public async Task ConcurrentActivationOnlyOneWriterWins()
    {
        var s = Store(); await s.UploadAsync(Bundle("a")); await s.UploadAsync(Bundle("b")); var fingerprint = (await s.StatusAsync()).Str("activeFingerprint");
        async Task<bool> Activate(string v) { try { await s.ActivateAsync(v, fingerprint, false, false); return true; } catch (RevisionConflictException) { return false; } }
        Assert.Single(await Task.WhenAll(Activate("a"), Activate("b")), x => x);
    }
    [Theory][InlineData("../bad")][InlineData("/bad")][InlineData("bad\\path")]
    public async Task InvalidVersionCannotEscapeStorage(string version) => await Assert.ThrowsAsync<ServiceException>(() => Store().UploadAsync(Bundle(version)));
    [Fact] public async Task InvalidBaseEmptyLanguageAndOversizeAreRejected()
    {
        var s = Store(); var bundle = Bundle("x"); bundle["baseVersion"] = "wrong"; await Assert.ThrowsAsync<ServiceException>(() => s.UploadAsync(bundle));
        await Assert.ThrowsAsync<ServiceException>(() => s.UploadAsync(Bundle("x", "")));
        await Assert.ThrowsAsync<ServiceException>(() => s.UploadAsync(Bundle("x", new string('a', 64001))));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

[Trait("Category", "Integration")]
public sealed class SkillAdminApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact] public async Task PostmanWorkflowRequiresAdminAndMakesNextPublicReadUseActivatedVersion()
    {
        using var factory = fixture.CreateFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["SKILL_ADMIN_TOKEN"] = new string('z', 40) })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/admin/planning/skills/status")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Skill-Admin-Token", new string('z', 40));
        var before = (await client.GetFromJsonAsync<JsonObject>("/admin/planning/skills/status"))!;
        var bundle = new JsonObject { ["version"] = "http.test", ["baseVersion"] = ItinerarySkill.BaseVersion, ["enAppendix"] = "Test addition.", ["zhHantAppendix"] = "新增測試。" };
        using var form = new MultipartFormDataContent(); form.Add(new StringContent(bundle.ToJsonString(), Encoding.UTF8, "application/json"), "file", "skill.json");
        (await client.PostAsync("/admin/planning/skills/versions", form)).EnsureSuccessStatusCode();
        Assert.Equal(ItinerarySkill.BaseVersion, (await client.GetFromJsonAsync<JsonObject>("/planning/skills"))!.Str("version"));
        (await client.PostAsJsonAsync("/admin/planning/skills/versions/http.test/activate", new { expectedActiveFingerprint = before.Str("activeFingerprint") })).EnsureSuccessStatusCode();
        var read = (await client.GetFromJsonAsync<JsonObject>("/planning/skills?language=en"))!;
        Assert.Equal("http.test", read.Str("version")); Assert.EndsWith("Test addition.", read.Str("content"));
        // Admin recovery remains available if an operator placed a malformed active file.
        File.WriteAllText(Path.Combine(fixture.TemporaryRoot, "server", "planning-skills", "active.json"), "{");
        var invalid = (await client.GetFromJsonAsync<JsonObject>("/admin/planning/skills/status"))!;
        Assert.False(invalid.Bool("activeValid"));
        (await client.PostAsJsonAsync("/admin/planning/skills/rollback", new { version = ItinerarySkill.BaseVersion, expectedActiveFingerprint = invalid.Str("activeFingerprint"), confirmRemoval = true })).EnsureSuccessStatusCode();
        Assert.Equal(ItinerarySkill.BaseVersion, (await client.GetFromJsonAsync<JsonObject>("/planning/skills"))!.Str("version"));
        var baseline = ItinerarySkill.Load(null);
        using var documents = new MultipartFormDataContent();
        documents.Add(new StringContent("markdown.test"), "version"); documents.Add(new StringContent(ItinerarySkill.BaseVersion), "baseVersion");
        documents.Add(new StringContent(baseline.English + "\n\nNew Markdown rule.", Encoding.UTF8), "en", "itinerary.en.md");
        documents.Add(new StringContent(baseline.Chinese + "\n\n新增 Markdown 規則。", Encoding.UTF8), "zhHant", "itinerary.zh-Hant.md");
        (await client.PostAsync("/admin/planning/skills/documents", documents)).EnsureSuccessStatusCode();
        var uploaded = (await client.GetFromJsonAsync<JsonObject>("/admin/planning/skills/versions/markdown.test"))!;
        Assert.Equal("New Markdown rule.", uploaded.Object("bundle")!.Str("enAppendix"));
        Assert.True(uploaded.Bool("appendOnly"));
    }
}
