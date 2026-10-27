using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SydneyBuddy.Api;

/// One API instance owns mutations. Immutable versions and compare-and-swap prevent lost updates.
public sealed class SkillAdministration(ProjectPaths paths, IConfiguration configuration)
{
    readonly SemaphoreSlim gate = new(1, 1);
    public const int MaxBytes = 150_000;
    public string ActivePath => configuration["ITINERARY_SKILL_BUNDLE"] is { Length: > 0 } configured
        ? Path.GetFullPath(configured) : Path.Combine(paths.DataDirectory, "planning-skills", "active.json");
    string Active => ActivePath;
    string DirectoryPath => Path.GetDirectoryName(Active)!;
    string Versions => Path.Combine(DirectoryPath, "versions");
    public bool Authorized(string supplied)
    {
        var secret = configuration["SKILL_ADMIN_TOKEN"] ?? "";
        if (configuration["SKILL_ADMIN_TOKEN_FILE"] is { Length: > 0 } file)
        {
            try { secret = File.ReadAllText(file).Trim(); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        return secret.Length >= 32 && supplied.Length is >= 32 and <= 512 &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    }
    static void CheckVersion(string version)
    {
        if (!Regex.IsMatch(version, @"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}\z"))
            throw new ServiceException("Invalid Skill version.");
    }
    string VersionPath(string version) { CheckVersion(version); return Path.Combine(Versions, version + ".json"); }
    static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static void ValidateBundle(JsonObject value)
    {
        var version = value.Str("version"); CheckVersion(version);
        if (value.Str("baseVersion") != ItinerarySkill.BaseVersion || version == ItinerarySkill.BaseVersion)
            throw new ServiceException("Keep baseVersion and choose a new version.");
        foreach (var key in new[] { "enAppendix", "zhHantAppendix" })
            if (value[key] is not JsonValue text || !text.TryGetValue<string>(out var content) || string.IsNullOrWhiteSpace(content) || Encoding.UTF8.GetByteCount(content) > 64_000)
                throw new ServiceException("Provide nonempty English and Chinese appendices, each at most 64000 UTF-8 bytes.");
        if (Encoding.UTF8.GetByteCount(value.ToJsonString()) > MaxBytes) throw new ServiceException("Skill bundle exceeds 150000 bytes.");
        if (value.Any(p => p.Key is not ("version" or "baseVersion" or "enAppendix" or "zhHantAppendix")))
            throw new ServiceException("Unknown bundle fields. Only version, baseVersion and the two appendices are accepted.");
    }
    byte[]? ActiveBytes() => File.Exists(Active) ? File.ReadAllBytes(Active) : null;
    static string Fingerprint(byte[]? bytes) => bytes is null ? "embedded:" + ItinerarySkill.BaseVersion : Hash(bytes);
    JsonObject ReadVersion(string version)
    {
        if (version == ItinerarySkill.BaseVersion) return new() { ["version"] = version, ["baseVersion"] = version, ["enAppendix"] = "", ["zhHantAppendix"] = "" };
        var file = VersionPath(version);
        if (!File.Exists(file)) throw new ServiceException("Skill version not found.");
        var value = JsonNode.Parse(File.ReadAllBytes(file))!.AsObject(); ValidateBundle(value); return value;
    }
    static JsonObject? ParseActive(byte[]? bytes)
    {
        if (bytes is null) return null;
        var value = JsonNode.Parse(bytes)!.AsObject(); ValidateBundle(value); return value;
    }
    static bool Retains(JsonObject? previous, JsonObject next) => previous is null ||
        new[] { "enAppendix", "zhHantAppendix" }.All(k => next.Str(k).StartsWith(previous.Str(k), StringComparison.Ordinal));
    JsonObject Inspect(JsonObject target, byte[]? active)
    {
        JsonObject? previous;
        try { previous = ParseActive(active); }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or ServiceException)
        { return new() { ["version"] = target.Str("version"), ["activeFingerprint"] = Fingerprint(active), ["activeValid"] = false,
            ["appendOnly"] = false, ["removedRules"] = null, ["requiresRemovalConfirmation"] = true, ["note"] = "Active bundle is invalid. Review the target before explicitly recovering." }; }
        var removed = new JsonObject();
        foreach (var key in new[] { "enAppendix", "zhHantAppendix" })
        {
            var old = previous?.Str(key) ?? ""; var next = target.Str(key);
            // For a true prefix rollback, show exactly what would be removed. Otherwise show the old text in full.
            removed[key] = old.StartsWith(next, StringComparison.Ordinal) ? old[next.Length..] : next.StartsWith(old, StringComparison.Ordinal) ? "" : old;
        }
        var appendOnly = Retains(previous, target);
        return new() { ["version"] = target.Str("version"), ["activeVersion"] = previous?.Str("version") ?? ItinerarySkill.BaseVersion,
            ["activeFingerprint"] = Fingerprint(active), ["activeValid"] = true, ["appendOnly"] = appendOnly,
            ["requiresRemovalConfirmation"] = !appendOnly, ["removedRules"] = removed };
    }
    static void PrivateWrite(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options)) { stream.Write(content); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    void Remember(JsonObject value)
    {
        if (value.Str("version") == ItinerarySkill.BaseVersion) return;
        var path = VersionPath(value.Str("version"));
        if (File.Exists(path))
        {
            if (!JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllBytes(path)), value)) throw new RevisionConflictException();
            return;
        }
        PrivateWrite(path, Encoding.UTF8.GetBytes(value.ToJsonString()));
    }
    public async Task<JsonObject> StatusAsync()
    {
        await gate.WaitAsync();
        try
        {
            var bytes = ActiveBytes();
            var result = new JsonObject { ["activeFingerprint"] = Fingerprint(bytes), ["baseVersion"] = ItinerarySkill.BaseVersion };
            try { var active = ParseActive(bytes); result["activeVersion"] = active?.Str("version") ?? ItinerarySkill.BaseVersion; result["activeValid"] = true; }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or ServiceException) { result["activeValid"] = false; }
            return result;
        }
        finally { gate.Release(); }
    }
    public async Task<JsonObject> UploadAsync(JsonObject bundle)
    {
        ValidateBundle(bundle);
        await gate.WaitAsync();
        try { Remember(bundle); var preview = Inspect(bundle, ActiveBytes()); preview["uploaded"] = true; preview["activated"] = false; return preview; }
        finally { gate.Release(); }
    }
    public async Task<JsonObject> GetAsync(string version)
    {
        await gate.WaitAsync();
        try {
            var bytes = ActiveBytes();
            var bundle = version != ItinerarySkill.BaseVersion && !File.Exists(VersionPath(version)) && ParseActive(bytes) is {} active && active.Str("version") == version
                ? active : ReadVersion(version);
            var result = Inspect(bundle, bytes); result["bundle"] = bundle; return result;
        }
        finally { gate.Release(); }
    }
    public async Task<JsonObject> ListAsync()
    {
        await gate.WaitAsync();
        try
        {
            var list = new JsonArray(ItinerarySkill.BaseVersion);
            if (Directory.Exists(Versions)) foreach (var path in Directory.EnumerateFiles(Versions, "*.json").Order()) list.Add(Path.GetFileNameWithoutExtension(path));
            // Also expose an existing operator-installed version before the API's first activation.
            try { var active = ParseActive(ActiveBytes()); if (active is not null && !list.Any(x => x!.ToString() == active.Str("version"))) list.Add(active.Str("version")); }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or ServiceException) { }
            return new() { ["versions"] = list };
        }
        finally { gate.Release(); }
    }
    public async Task<JsonObject> TemplateAsync()
    {
        await gate.WaitAsync();
        try
        {
            var current = ParseActive(ActiveBytes());
            return new() { ["version"] = "CHOOSE-A-NEW-VERSION", ["baseVersion"] = ItinerarySkill.BaseVersion,
                ["enAppendix"] = current?.Str("enAppendix") ?? "", ["zhHantAppendix"] = current?.Str("zhHantAppendix") ?? "" };
        }
        finally { gate.Release(); }
    }
    public async Task<JsonObject> ActivateAsync(string version, string expected, bool rollback, bool confirmed)
    {
        await gate.WaitAsync();
        try
        {
            var previous = ActiveBytes();
            if (string.IsNullOrEmpty(expected) || expected != Fingerprint(previous)) throw new RevisionConflictException();
            JsonObject target;
            // An existing operator bundle can be read and archived even before its first API-managed update.
            try { var current = ParseActive(previous); if (current is not null) Remember(current); }
            catch (Exception e) when (rollback && (e is System.Text.Json.JsonException or InvalidOperationException or ServiceException)) { }
            target = ReadVersion(version);
            var preview = Inspect(target, previous);
            if (!rollback && (version == ItinerarySkill.BaseVersion || !preview.Bool("appendOnly") || !preview.Bool("activeValid")))
                throw new ServiceException("Activation may only append rules. Preview removals and use the explicit rollback endpoint to remove rules.");
            if (rollback && !confirmed) throw new ServiceException("Review removedRules, discuss removals, then explicitly set confirmRemoval=true.");
            // Keep raw old bundles, including malformed operator files, for recovery review.
            if (previous is not null) PrivateWrite(Path.Combine(DirectoryPath, "history", Hash(previous) + ".json"), previous);
            if (version == ItinerarySkill.BaseVersion) { if (File.Exists(Active)) File.Delete(Active); }
            else PrivateWrite(Active, Encoding.UTF8.GetBytes(target.ToJsonString()));
            var result = Inspect(target, ActiveBytes()); result["activated"] = true; result["action"] = rollback ? "rollback" : "activate"; return result;
        }
        finally { gate.Release(); }
    }
}
