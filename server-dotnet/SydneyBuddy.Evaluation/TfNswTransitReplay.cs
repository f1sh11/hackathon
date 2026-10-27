using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using SydneyBuddy.Api;

/// <summary>
/// Captures one deliberately small public TfNSW journey and proves that the
/// scheduler consumes its historical evidence. This is evaluator-only: an old
/// snapshot can never be mistaken for a fresh production journey query.
/// </summary>
internal static class TfNswTransitReplay
{
    private const string Provider = "Transport for NSW / Trip Planner";
    private const string Mode = "historical-tfnsw-transit-replay";
    private const string TimeZone = "Australia/Sydney";
    private static readonly JourneyPoint From = new("Central Station", -33.8827, 151.2069, 0);
    private static readonly JourneyPoint To = new("Circular Quay", -33.8610, 151.2127, 0);

    public static async Task<JsonObject> CaptureAsync(string fixturePath, SecretStore secrets,
        DateOnly? requestedDate, int? requestedMinute, CancellationToken ct = default)
    {
        var (date, minute, departure) = RequestedDeparture(requestedDate, requestedMinute);
        using var transportHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        using var roadHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var provider = new NswJourneyProvider(transportHttp, secrets,
            new NavigationService(roadHttp, new ConfigurationBuilder().Build()));

        // The provider owns the credential header. Neither raw request nor raw
        // response is persisted; only this normalized, public route is stored.
        var steps = await provider.RouteAsync(From, To, "transit", departure, ct);
        var capturedAt = DateTimeOffset.UtcNow;
        var fixture = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["mode"] = Mode,
            ["provider"] = Provider,
            ["captureClient"] = "SydneyBuddy.Evaluation --capture-tfnsw-replay",
            ["capturedAt"] = capturedAt.ToString("O", CultureInfo.InvariantCulture),
            ["timeZone"] = TimeZone,
            ["notLiveNavigation"] = true,
            ["usesDeviceLocation"] = false,
            ["endpointBinding"] = "Fixed public Central Station and Circular Quay coordinates; no account, device location, request header, or credential is stored.",
            ["requestedDeparture"] = new JsonObject
            {
                ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["minute"] = minute,
                ["iso"] = departure.ToString("O", CultureInfo.InvariantCulture)
            },
            ["note"] = "A fixed historical TfNSW Trip Planner snapshot for evaluator T01. It is not a live timetable or navigation query, and fares remain unavailable.",
            ["routes"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "T01",
                    ["from"] = Point(From),
                    ["to"] = Point(To),
                    ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["minute"] = minute,
                    ["leg"] = NormalizeLeg(steps, departure, capturedAt)
                }
            }
        };
        EnsureNoCredentialFields(fixture);
        await File.WriteAllTextAsync(fixturePath, fixture.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
        return fixture;
    }

    public static TransitReplayResult Verify(JsonObject fixture, ScheduleEditorService editor)
    {
        try
        {
            EnsureNoCredentialFields(fixture);
            if (fixture.Int("schemaVersion") != 1 || fixture.Str("mode") != Mode || fixture.Str("provider") != Provider ||
                fixture.Str("timeZone") != TimeZone || !fixture.Bool("notLiveNavigation") || fixture.Bool("usesDeviceLocation"))
                throw new InvalidDataException("TfNSW replay manifest is missing its historical, public-route boundary.");
            if (!DateTimeOffset.TryParse(fixture.Str("capturedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var capturedAt))
                throw new InvalidDataException("TfNSW replay manifest has no valid capture time.");
            var sydney = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, sydney).DateTime);
            if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(capturedAt, sydney).DateTime) > today)
                throw new InvalidDataException("TfNSW replay capture time cannot be in the future.");

            var request = fixture.Object("requestedDeparture") ?? throw new InvalidDataException("TfNSW replay is missing its fixed request time.");
            if (!DateOnly.TryParseExact(request.Str("date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                request.Int("minute", -1) is < 0 or > 1439 || !DateTimeOffset.TryParse(request.Str("iso"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var requestedDeparture))
                throw new InvalidDataException("TfNSW replay has an invalid request time.");
            var minute = request.Int("minute");
            var expectedDeparture = LocalDeparture(date, minute);
            if (requestedDeparture != expectedDeparture)
                throw new InvalidDataException("TfNSW replay request time does not match its Sydney date and minute.");

            var routes = fixture.Array("routes") ?? throw new InvalidDataException("TfNSW replay is missing route evidence.");
            if (routes.Count != 1 || routes[0] is not JsonObject route || route.Str("id") != "T01")
                throw new InvalidDataException("TfNSW replay must contain exactly the T01 route.");
            var from = route.Object("from") ?? throw new InvalidDataException("TfNSW replay is missing its origin.");
            var to = route.Object("to") ?? throw new InvalidDataException("TfNSW replay is missing its destination.");
            ValidateFixedPoint(from, From);
            ValidateFixedPoint(to, To);
            if (route.Str("date") != date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) || route.Int("minute", -1) != minute)
                throw new InvalidDataException("TfNSW replay route does not match its fixed request time.");
            var leg = route.Object("leg") ?? throw new InvalidDataException("TfNSW replay is missing a normalized leg.");
            ValidateLeg(leg, expectedDeparture);

            // Same keying and structural validation used by the schedule editor.
            var evidence = new TransportEvidence(routes, allowHistorical: true);
            if (evidence.Get(from, to, route.Str("date"), route.Int("minute")) is null)
                throw new InvalidDataException("TfNSW replay could not be looked up by endpoint and time key.");

            var scheduled = BuildScheduleProof(route, leg, editor);
            var scheduledLeg = scheduled.Array("days")?[0]?.AsObject().Array("legs")?.FirstOrDefault() as JsonObject;
            if (scheduledLeg is null || scheduledLeg.Str("mode") != "transit" || scheduledLeg.Str("source") != Provider ||
                scheduledLeg.Array("steps")?.Count != leg.Array("steps")?.Count)
                throw new InvalidDataException("The fixed TfNSW evidence was not used by the schedule editor.");

            var nonWalking = leg.Array("steps")!.OfType<JsonObject>().Where(step => step.Str("mode") != "walk").ToArray();
            var lines = nonWalking.Select(step => step.Str("line")).Where(line => line.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            var artifact = new JsonObject
            {
                ["id"] = "T01",
                ["passed"] = true,
                ["provider"] = Provider,
                ["mode"] = Mode,
                ["capturedAt"] = fixture["capturedAt"]?.DeepClone(),
                ["requestedDeparture"] = fixture["requestedDeparture"]?.DeepClone(),
                ["from"] = from.DeepClone(),
                ["to"] = to.DeepClone(),
                ["transitStepCount"] = nonWalking.Length,
                ["transitLines"] = new JsonArray(lines.Select(line => (JsonNode)JsonValue.Create(line)!).ToArray()),
                ["scheduledLeg"] = new JsonObject
                {
                    ["mode"] = scheduledLeg.Str("mode"),
                    ["source"] = scheduledLeg.Str("source"),
                    ["minutes"] = scheduledLeg.Int("minutes"),
                    ["walkingMeters"] = scheduledLeg.Number("walkingMeters")
                },
                ["boundary"] = "Historical fixed replay only; it does not assert a live timetable, live navigation, fare, account, device location, or credential.",
                ["summary"] = "T01 passed: the normalized TfNSW snapshot contains public-transit steps and the schedule editor consumed that transit leg."
            };
            return new TransitReplayResult(true, artifact.Str("summary"), artifact);
        }
        catch (Exception error) when (error is InvalidDataException or ServiceException or ArgumentException or InvalidOperationException)
        {
            var message = error is ServiceException ? error.Message : "TfNSW fixed replay validation failed: " + error.Message;
            return TransitReplayResult.Failed(message);
        }
    }

    private static JsonObject BuildScheduleProof(JsonObject route, JsonObject leg, ScheduleEditorService editor)
    {
        var date = route.Str("date");
        var minute = route.Int("minute");
        var endMinute = Math.Min(1440, minute + Math.Max(120, leg.Int("minutes") + 30));
        if (endMinute <= minute) throw new InvalidDataException("TfNSW replay does not leave a usable schedule window.");
        var profile = new JsonObject
        {
            ["date"] = date,
            ["days"] = 1,
            ["budget"] = 100,
            ["maxSteps"] = 60000,
            ["startMinute"] = minute,
            ["endMinute"] = endMinute,
            ["request"] = "T01 fixed TfNSW transit replay",
            ["transportPreference"] = "lessWalking",
            ["interests"] = new JsonArray(),
            ["mustVisit"] = new JsonArray(),
            ["start"] = route.Object("from")!.DeepClone(),
            ["end"] = route.Object("to")!.DeepClone()
        };
        var request = new JsonObject
        {
            ["trip"] = new JsonObject
            {
                ["title"] = "TfNSW fixed transit replay",
                ["mode"] = "demo",
                ["profile"] = profile,
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["trace"] = new JsonArray(),
                ["warnings"] = new JsonArray(),
                ["weather"] = new JsonObject { ["summary"] = "Fixed replay", ["rainy"] = false, ["source"] = "evaluation fixture" }
            },
            ["settings"] = new JsonObject
            {
                ["pace"] = "balanced",
                ["timeLimit"] = 240,
                ["restroomInterval"] = 360,
                ["mealCost"] = 25,
                ["dailyBudget"] = 100,
                ["totalBudget"] = 100,
                ["maxSteps"] = 60000,
                ["luggage"] = false
            },
            ["transportRoutes"] = new JsonArray(route.DeepClone()),
            ["days"] = new JsonArray(new JsonObject
            {
                ["date"] = date,
                ["startMinute"] = minute,
                ["endMinute"] = endMinute,
                ["activities"] = new JsonArray()
            })
        };
        return editor.Build(request, allowHistoricalTransit: true);
    }

    private static JsonObject NormalizeLeg(IReadOnlyList<JourneyStep> steps, DateTimeOffset departure, DateTimeOffset capturedAt)
    {
        if (steps.Count == 0 || !steps.Any(step => step.Mode != "walk"))
            throw new ServiceException("NSW did not return a public-transit journey for the fixed replay.");
        var arrival = steps[^1].ArrivalTime;
        if (arrival < departure) throw new ServiceException("NSW returned an invalid journey time for the fixed replay.");
        var serialisedSteps = JsonSerializer.SerializeToNode(steps, JsonExtensions.Options) as JsonArray
            ?? throw new InvalidDataException("Could not serialize the TfNSW replay steps.");
        return new JsonObject
        {
            ["meters"] = (int)Math.Ceiling(steps.Sum(step => step.Meters)),
            ["walkingMeters"] = (int)Math.Ceiling(steps.Where(step => step.Mode == "walk").Sum(step => step.Meters)),
            ["minutes"] = (int)Math.Ceiling((arrival - departure).TotalMinutes),
            ["source"] = Provider,
            ["mode"] = "transit",
            ["polyline"] = "",
            ["departureTime"] = departure.ToString("O", CultureInfo.InvariantCulture),
            ["arrivalTime"] = arrival.ToString("O", CultureInfo.InvariantCulture),
            ["fetchedAt"] = capturedAt.ToString("O", CultureInfo.InvariantCulture),
            ["steps"] = serialisedSteps,
            ["fareKnown"] = false,
            ["transfers"] = Math.Max(0, steps.Count(step => step.Mode != "walk") - 1),
            ["reason"] = "Fixed TfNSW historical replay; the request compared no synthetic transit substitute and fares are unavailable."
        };
    }

    private static void ValidateLeg(JsonObject leg, DateTimeOffset requestedDeparture)
    {
        if (leg.Str("mode") != "transit" || leg.Str("source") != Provider || leg.Number("meters", -1) < leg.Number("walkingMeters", -1) ||
            leg.Number("walkingMeters", -1) < 0 || !DateTimeOffset.TryParse(leg.Str("departureTime"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var departure) ||
            !DateTimeOffset.TryParse(leg.Str("arrivalTime"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var arrival) ||
            !DateTimeOffset.TryParse(leg.Str("fetchedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) || arrival < departure ||
            departure != requestedDeparture || leg.Int("minutes", -1) != (int)Math.Ceiling((arrival - departure).TotalMinutes))
            throw new InvalidDataException("TfNSW replay leg has inconsistent transport timing or distance.");
        var steps = leg.Array("steps")?.OfType<JsonObject>().ToArray() ?? [];
        if (steps.Length == 0 || steps.All(step => step.Str("mode") == "walk"))
            throw new InvalidDataException("TfNSW replay has no public-transit step.");
        var cursor = departure;
        foreach (var step in steps)
        {
            if (step.Str("mode").Length == 0 || step.Str("from").Length == 0 || step.Str("to").Length == 0 ||
                !DateTimeOffset.TryParse(step.Str("departureTime"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start) ||
                !DateTimeOffset.TryParse(step.Str("arrivalTime"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end) || start < cursor || end < start)
                throw new InvalidDataException("TfNSW replay contains an invalid or discontinuous transit step.");
            if (step.Str("mode") != "walk" && step.Str("line").Length == 0)
                throw new InvalidDataException("TfNSW replay transit step is missing its public service line.");
            cursor = end;
        }
        if (cursor != arrival) throw new InvalidDataException("TfNSW replay step times do not reach the recorded arrival.");
    }

    private static void ValidateFixedPoint(JsonObject point, JourneyPoint expected)
    {
        if (point.Str("name") != expected.Name || Math.Abs(point.Number("lat") - expected.Lat) > 0.000001 ||
            Math.Abs(point.Number("lng") - expected.Lng) > 0.000001)
            throw new InvalidDataException("TfNSW replay endpoint does not match the fixed public landmark manifest.");
    }

    private static void EnsureNoCredentialFields(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var entry in obj)
            {
                var name = entry.Key.ToLowerInvariant();
                if (name.Contains("token", StringComparison.Ordinal) || name.Contains("apikey", StringComparison.Ordinal) ||
                    name.Contains("authorization", StringComparison.Ordinal) || name.Contains("credential", StringComparison.Ordinal) ||
                    name.Contains("secret", StringComparison.Ordinal) || name == "key")
                    throw new InvalidDataException("TfNSW replay must not contain credentials or request headers.");
                EnsureNoCredentialFields(entry.Value);
            }
        }
        else if (node is JsonArray items)
        {
            foreach (var item in items) EnsureNoCredentialFields(item);
        }
    }

    private static (DateOnly Date, int Minute, DateTimeOffset Departure) RequestedDeparture(DateOnly? requestedDate, int? requestedMinute)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var date = requestedDate ?? DateOnly.FromDateTime(now.DateTime);
        var minute = requestedMinute ?? (int)Math.Ceiling((now.Hour * 60 + now.Minute + 30) / 15d) * 15;
        if (minute > 1439)
        {
            date = date.AddDays(1);
            minute = 600;
        }
        if (minute is < 0 or > 1439) throw new ArgumentException("--transit-minute must be between 0 and 1439.");
        return (date, minute, LocalDeparture(date, minute));
    }

    private static DateTimeOffset LocalDeparture(DateOnly date, int minute)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static JsonObject Point(JourneyPoint point) => new()
    {
        ["name"] = point.Name,
        ["lat"] = point.Lat,
        ["lng"] = point.Lng
    };
}

internal sealed record TransitReplayResult(bool Passed, string Summary, JsonObject Artifact)
{
    public static TransitReplayResult Failed(string message) => new(false, message, new JsonObject
    {
        ["id"] = "T01",
        ["passed"] = false,
        ["provider"] = "Transport for NSW / Trip Planner",
        ["summary"] = message,
        ["boundary"] = "TfNSW historical fixed replay was not verified; no live-timetable claim is made."
    });
}
