using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SydneyBuddy.Api;

return await EvaluationProgram.RunAsync(args);

internal static class EvaluationProgram
{
    private static readonly string[] RequiredCaseColumns = ["case_id", "interests", "max_steps", "energy", "rhythm"];
    private static readonly string[] RequiredExpectationColumns = ["case_id", "outcome", "expected_behavior"];

    public static async Task<int> RunAsync(string[] args)
    {
        var options = Options.Parse(args);
        var root = options.Root ?? ProjectPaths.DiscoverRoot();
        var dataDirectory = Path.Combine(root, "evaluation", "data");
        var casesPath = Path.Combine(dataDirectory, "cases.json");
        var habitsPath = Path.Combine(dataDirectory, "user-habits.csv");
        var expectationsPath = Path.Combine(dataDirectory, "expected-checks.csv");
        var transportPath = Path.Combine(dataDirectory, "transport-evidence.json");
        var recordedWalkingReplayPath = options.WalkingReplay ?? Path.Combine(dataDirectory, "apple-mapkit-walking-replay.json");
        var cases = JsonNode.Parse(await File.ReadAllTextAsync(casesPath)) as JsonArray
            ?? throw new InvalidDataException("evaluation/data/cases.json must be a JSON array.");
        if (cases.Count != 10) throw new InvalidDataException("The evaluation suite must contain exactly 10 cases.");
        ValidateHabits(habitsPath, cases);
        ValidateExpectationChecks(expectationsPath, cases);
        var recordedWalkingReplay = JsonNode.Parse(await File.ReadAllTextAsync(recordedWalkingReplayPath)) as JsonObject
            ?? throw new InvalidDataException("The selected walking replay must be a JSON object.");
        ValidateRecordedWalkingReplay(root, recordedWalkingReplay);

        var output = options.Output ?? Path.Combine(root, "evaluation", "output",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "cases"));

        using var catalogCache = new CatalogCache();
        var paths = new ProjectPaths(root);
        var secrets = new SecretStore(paths);
        var transportFixture = options.CaptureTfNswReplay
            ? await TfNswTransitReplay.CaptureAsync(transportPath, secrets, options.TransitDate, options.TransitMinute)
            : JsonNode.Parse(await File.ReadAllTextAsync(transportPath)) as JsonObject
                ?? throw new InvalidDataException("evaluation/data/transport-evidence.json must be a JSON object.");
        var catalog = new PlaceCatalog(catalogCache, paths);
        var localPlanner = CreatePlanner(catalog, paths, new BlockedExternalClient());
        var aiConfigured = options.WithAi && !string.IsNullOrWhiteSpace(secrets.Read()["NVIDIA_API_KEY"]);
        using var aiHttp = aiConfigured ? new HttpClient() : null;
        var aiPlanner = aiConfigured
            ? CreatePlanner(catalog, paths, new ExternalJsonClient(aiHttp!))
            : null;
        var editor = new ScheduleEditorService(catalog);
        var transitReplay = TfNswTransitReplay.Verify(transportFixture, editor);
        await WriteJsonAsync(Path.Combine(output, "tfnsw-transit-replay.json"), transitReplay.Artifact);

        var results = new List<CaseResult>();
        foreach (var node in cases)
        {
            var testCase = node as JsonObject ?? throw new InvalidDataException("Each evaluation case must be an object.");
            var local = await RunCaseAsync(testCase, "local_rule", localPlanner, editor, catalog, recordedWalkingReplay);
            results.Add(local);
            WriteCaseArtifact(output, local);

            var ai = !AppliesToStrategy(testCase, "ai_plus_rules")
                ? CaseResult.NotApplicable(testCase, "ai_plus_rules", "This recorded-road replay exercises local remaining-journey logic only; no model choice is involved.")
                : aiPlanner is null
                ? CaseResult.NotRun(testCase, "ai_plus_rules", options.WithAi
                    ? "NVIDIA_API_KEY is not configured; the AI comparison was not run."
                    : "AI comparison disabled. Re-run with --with-ai after configuring NVIDIA_API_KEY.")
                : await RunCaseAsync(testCase, "ai_plus_rules", aiPlanner, editor, catalog, recordedWalkingReplay);
            results.Add(ai);
            WriteCaseArtifact(output, ai);
        }

        var aiResults = results.Where(result => result.Strategy == "ai_plus_rules" && result.Status != "not_applicable").ToArray();
        var configuredModel = secrets.Read().GetValueOrDefault("NVIDIA_MODEL", SecretStore.DefaultModel);
        if (string.IsNullOrWhiteSpace(configuredModel)) configuredModel = SecretStore.DefaultModel;
        var aiExecuted = aiResults.Count(result => result.Output?.Array("trace")?.OfType<JsonObject>()
            .Any(trace => trace.Str("source") == "NVIDIA NIM") == true);
        var ruleRejectedBeforeModel = aiResults.Count(result => result.Status == "expected_rejection" &&
            result.Output?.Array("trace")?.OfType<JsonObject>().Any(trace => trace.Str("source") == "NVIDIA NIM") != true);
        WriteCatalogSnapshot(output, catalog.Get());
        await WriteJsonAsync(Path.Combine(output, "run-metadata.json"), new JsonObject
        {
            ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["caseCount"] = cases.Count,
            ["localStrategy"] = "demo mode: current local recommendation and deterministic scheduling",
            ["aiStrategy"] = aiConfigured
                ? "assisted mode: current NVIDIA recommendation followed by the same deterministic scheduler"
                : "not run; no model result is substituted or simulated",
            ["aiComparison"] = new JsonObject
            {
                ["requested"] = options.WithAi,
                ["configured"] = aiConfigured,
                ["provider"] = aiConfigured ? "NVIDIA NIM" : null,
                ["model"] = aiConfigured ? configuredModel : null,
                ["applicableCaseCount"] = aiResults.Length,
                ["providerExecutedCaseCount"] = aiExecuted,
                ["ruleRejectedBeforeModelCaseCount"] = ruleRejectedBeforeModel,
                ["passedCaseCount"] = aiResults.Count(result => result.Passed),
                ["notRunCaseCount"] = aiResults.Count(result => result.Status == "not_run"),
                ["comparisonBoundary"] = "The same fixed catalog, input profile, demo weather and empty evaluation memory are used; the model ranks known IDs while deterministic rules validate the itinerary."
            },
            ["transportFixture"] = new JsonObject
            {
                ["mode"] = transportFixture["mode"]?.DeepClone(),
                ["provider"] = transportFixture["provider"]?.DeepClone(),
                ["capturedAt"] = transportFixture["capturedAt"]?.DeepClone(),
                ["routeCount"] = transportFixture.Array("routes")?.Count,
                ["note"] = transportFixture["note"]?.DeepClone()
            },
            ["transitReplay"] = transitReplay.Artifact.DeepClone(),
            ["recordedWalkingReplay"] = new JsonObject
            {
                ["provider"] = recordedWalkingReplay["provider"]?.DeepClone(),
                ["recordedDate"] = recordedWalkingReplay["recordedDate"]?.DeepClone(),
                ["capturedAt"] = recordedWalkingReplay["capturedAt"]?.DeepClone(),
                ["captureClient"] = recordedWalkingReplay["captureClient"]?.DeepClone(),
                ["sourceFixture"] = recordedWalkingReplay["sourceFixture"]?.DeepClone(),
                ["notLiveNavigation"] = recordedWalkingReplay["notLiveNavigation"]?.DeepClone(),
                ["legCount"] = recordedWalkingReplay.Array("legs")?.Count,
                ["note"] = recordedWalkingReplay["note"]?.DeepClone()
            }
        });
        await File.WriteAllTextAsync(Path.Combine(output, "results.csv"), RenderCsv(results), new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(output, "report.html"), RenderHtml(results, aiConfigured, recordedWalkingReplay, transitReplay), new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(output, "issues.md"), RenderIssues(results, recordedWalkingReplay, transitReplay), new UTF8Encoding(false));

        var localResults = results.Where(x => x.Strategy == "local_rule").ToArray();
        var localFailures = localResults.Where(x => !x.Passed).ToArray();
        var localRoutes = localResults.Count(x => x.Status == "passed");
        var localExpectedRejections = localResults.Count(x => x.Status == "expected_rejection");
        var aiFailures = results.Where(x => x.Strategy == "ai_plus_rules" && x.Status != "not_applicable" && !x.Passed).ToArray();
        Console.WriteLine($"Evaluation report written to {output}");
        Console.WriteLine($"Local-rule assertions: {localResults.Count(x => x.Passed)}/10 matched ({localRoutes} routes; {localExpectedRejections} expected safety rejections).");
        Console.WriteLine(aiConfigured
            ? $"AI + rule assertions: {aiResults.Count(x => x.Passed)}/{aiResults.Length} passed; NVIDIA was actually called for {aiExecuted}/{aiResults.Length} cases ({ruleRejectedBeforeModel} safely rejected by rules before a model call)."
            : "AI + rule cases: not run (no model result was fabricated).");
        Console.WriteLine(transitReplay.Passed
            ? "TfNSW fixed transit replay: T01 passed; the schedule editor consumed the recorded transit evidence."
            : "TfNSW fixed transit replay: T01 failed; see tfnsw-transit-replay.json.");
        return localFailures.Length == 0 && transitReplay.Passed && (!options.WithAi || aiFailures.Length == 0) ? 0 : 1;
    }

    private static Planner CreatePlanner(PlaceCatalog catalog, ProjectPaths paths, IExternalJsonClient external) => new(
        new PlaceService(external, catalog),
        new RecommendationService(external, new EmptyMemoryService()),
        new RouteScheduler(new RouteService(external)),
        new WeatherService(external),
        new SecretStore(paths));

    private static async Task<CaseResult> RunCaseAsync(JsonObject testCase, string strategy, Planner planner,
        ScheduleEditorService editor, PlaceCatalog catalog, JsonObject recordedWalkingReplay)
    {
        if (testCase.Str("scenario") == "remaining_with_recorded_walking_replay")
            return RunRecordedWalkingReplayCase(testCase, strategy, editor, catalog, recordedWalkingReplay);
        var expected = testCase.Object("expected") ?? new JsonObject { ["outcome"] = "success" };
        try
        {
            var trip = await planner.PlanAsync((JsonObject)testCase.Object("profile")!.DeepClone(),
                strategy == "local_rule" ? "demo" : "assisted");
            if (testCase.Str("scenario") == "remaining_without_walking_evidence")
            {
                var request = (JsonObject)testCase.Object("remainingRequest")!.DeepClone();
                trip = RemainingJourney.Build(trip, request, editor, catalog);
            }
            var assessment = AssessSuccess(trip, expected);
            return new CaseResult(testCase, strategy, assessment.Passed ? "passed" : "failed", assessment.Passed, assessment.Summary, trip, null);
        }
        catch (ServiceException error)
        {
            var expectedError = expected.Str("outcome") == "error" && error.Message.Contains(expected.Str("messageContains"), StringComparison.Ordinal);
            return new CaseResult(testCase, strategy, expectedError ? "expected_rejection" : "failed", expectedError,
                expectedError ? "Expected constraint or evidence failure." : "Unexpected service failure.", null, error.Message);
        }
    }

    private static CaseResult RunRecordedWalkingReplayCase(JsonObject testCase, string strategy,
        ScheduleEditorService editor, PlaceCatalog catalog, JsonObject replay)
    {
        var expected = testCase.Object("expected") ?? new JsonObject { ["outcome"] = "success" };
        try
        {
            var profile = (JsonObject)testCase.Object("profile")!.DeepClone();
            var routes = BuildRecordedWalkingRoutes(replay, profile, catalog);
            var original = BuildRecordedReplayTrip(profile, routes, editor,
                testCase.Array("replayActivities") ?? throw new InvalidDataException("Recorded replay case is missing replayActivities."));
            var originalJson = original.ToJsonString();
            var firstStop = original.Array("days")![0]!.AsObject().Array("stops")![0]!.AsObject();
            var request = new JsonObject
            {
                ["day"] = 0,
                ["request"] = "我累了，少走一点",
                ["currentMinute"] = expected.Int("currentMinute", 640),
                ["origin"] = new JsonObject
                {
                    ["name"] = firstStop.Str("name"), ["lat"] = firstStop.Number("lat"), ["lng"] = firstStop.Number("lng")
                },
                ["completedIds"] = new JsonArray(firstStop.Str("id")),
                ["skippedIds"] = new JsonArray(),
                ["walkingRoutes"] = routes.DeepClone()
            };

            if (expected.Bool("assertMissingEvidenceRejected"))
            {
                var withoutEvidence = (JsonObject)request.DeepClone();
                withoutEvidence.Remove("walkingRoutes");
                try
                {
                    RemainingJourney.Build(original, withoutEvidence, editor, catalog);
                    return new CaseResult(testCase, strategy, "failed", false,
                        "Removing the recorded road evidence unexpectedly still produced a remaining-journey proposal.", original, null);
                }
                catch (ServiceException error) when (error.Message.Contains("请先查询真实步行时间", StringComparison.Ordinal))
                {
                    // The safety preflight is intentionally part of this successful replay case.
                }
            }

            var updated = RemainingJourney.Build(original, request, editor, catalog, allowHistoricalWalkingReplay: true);
            if (!StringComparer.Ordinal.Equals(originalJson, original.ToJsonString()))
                return new CaseResult(testCase, strategy, "failed", false,
                    "Remaining-journey replanning mutated the original itinerary instead of returning a proposal.", updated, null);
            updated["evaluationReplay"] = new JsonObject
            {
                ["provider"] = replay.Str("provider"), ["recordedDate"] = replay.Str("recordedDate"),
                ["sourceFixture"] = ReplayIdentity(replay),
                ["preFatigueSteps"] = original.Array("days")![0]!.AsObject().Int("estimatedSteps"),
                ["postFatigueSteps"] = updated.Array("days")![0]!.AsObject().Int("estimatedSteps"),
                ["initialStopIds"] = new JsonArray(original.Array("days")![0]!.AsObject().Array("stops")!.OfType<JsonObject>().Select(stop => (JsonNode)JsonValue.Create(stop.Str("id"))!).ToArray()),
                ["remainingStopIds"] = new JsonArray(updated.Array("days")![0]!.AsObject().Array("stops")!.OfType<JsonObject>().Select(stop => (JsonNode)JsonValue.Create(stop.Str("id"))!).ToArray())
            };

            var assessment = AssessSuccess(updated, expected);
            if (assessment.Passed)
                assessment = AssessRecordedReplay(updated, original, firstStop, expected, replay);
            return new CaseResult(testCase, strategy, assessment.Passed ? "passed" : "failed", assessment.Passed,
                assessment.Summary, updated, null);
        }
        catch (ServiceException error)
        {
            return new CaseResult(testCase, strategy, "failed", false, "Historical road replay could not produce a feasible remaining-journey proposal.", null, error.Message);
        }
    }

    private static JsonObject BuildRecordedReplayTrip(JsonObject profile, JsonArray routes,
        ScheduleEditorService editor, JsonArray replayActivities)
    {
        var requiredOrder = new[] { "garden", "opera", "rocks", "mca" };
        var definitions = replayActivities.OfType<JsonObject>().ToArray();
        if (definitions.Length != requiredOrder.Length || definitions.Select(item => item.Str("id")).Where((id, index) => id != requiredOrder[index]).Any())
            throw new InvalidDataException("Recorded replay activities must be garden, opera, rocks, and mca in that order.");
        var activities = new JsonArray(definitions.Select(item =>
        {
            var duration = item.Int("duration");
            if (duration is < 5 or > 240) throw new InvalidDataException("Recorded replay visit duration must be between 5 and 240 minutes.");
            return (JsonNode)new JsonObject
            {
                ["id"] = item.Str("id"), ["placeId"] = item.Str("id"), ["kind"] = "attraction", ["duration"] = duration,
                ["queueMinutes"] = 0, ["locked"] = item.Bool("locked")
            };
        }).ToArray());
        var seed = new JsonObject
        {
            ["title"] = "Recorded-road replay seed", ["mode"] = "demo", ["profile"] = profile.DeepClone(),
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["trace"] = new JsonArray(), ["warnings"] = new JsonArray(),
            ["weather"] = new JsonObject { ["summary"] = "固定回放，不查询实时天气", ["rainy"] = false, ["source"] = "evaluation fixture" },
            ["explanation"] = "Historical-road replay seed for remaining-journey evaluation."
        };
        var settings = new JsonObject
        {
            ["pace"] = "balanced", ["timeLimit"] = 1440, ["restroomInterval"] = 360,
            ["mealCost"] = 25, ["dailyBudget"] = profile.Number("budget"),
            ["totalBudget"] = profile.Number("budget") * profile.Int("days"),
            ["maxSteps"] = profile.Int("maxSteps"), ["luggage"] = false
        };
        return editor.Build(new JsonObject
        {
            ["trip"] = seed,
            ["settings"] = settings,
            ["days"] = new JsonArray(new JsonObject
            {
                ["date"] = profile.Str("date"), ["startMinute"] = profile.Int("startMinute"),
                ["endMinute"] = profile.Int("endMinute"), ["activities"] = activities
            }),
            ["walkingRoutes"] = routes.DeepClone()
        }, allowHistoricalWalkingReplay: true);
    }

    private static JsonArray BuildRecordedWalkingRoutes(JsonObject replay, JsonObject profile, PlaceCatalog catalog)
    {
        var points = catalog.Get().ToDictionary(place => place.Str("id"), place => place, StringComparer.Ordinal);
        points["start"] = profile.Object("start")!;
        points["end"] = profile.Object("end")!;
        var routes = new JsonArray();
        foreach (var node in replay.Array("legs")!.OfType<JsonObject>())
        {
            var fromId = node.Str("from");
            var toId = node.Str("to");
            if (!points.TryGetValue(fromId, out var from) || !points.TryGetValue(toId, out var to))
                throw new InvalidDataException($"Recorded walking replay references an unknown endpoint: {fromId} -> {toId}.");
            routes.Add(new JsonObject
            {
                ["from"] = from.DeepClone(), ["to"] = to.DeepClone(),
                ["meters"] = node.Number("meters"), ["seconds"] = node.Number("seconds"),
                ["source"] = replay.Str("provider") + " historical recorded replay",
                ["sourceFixture"] = ReplayIdentity(replay),
                ["recordedReplay"] = true, ["recordedDate"] = replay.Str("recordedDate"),
                ["replayKind"] = node.Str("kind"), ["replayLegId"] = node.Str("id"), ["derivedFrom"] = node["derivedFrom"]?.DeepClone()
            });
        }
        return routes;
    }

    private static Assessment AssessRecordedReplay(JsonObject updated, JsonObject original, JsonObject firstStop,
        JsonObject expected, JsonObject replay)
    {
        var day = updated.Array("days")![0]!.AsObject();
        var originalDay = original.Array("days")![0]!.AsObject();
        if (expected["minimumOriginalSteps"] is not null && originalDay.Int("estimatedSteps") < expected.Int("minimumOriginalSteps"))
            return new Assessment(false, "The pre-fatigue replay seed did not exceed the threshold that should force a change.");
        if (expected.Bool("preserveCompleted"))
        {
            var completed = day.Array("completedStops")?.OfType<JsonObject>().FirstOrDefault();
            if (completed is null || !SameRecordedStop(firstStop, completed))
                return new Assessment(false, "The completed activity was not preserved unchanged in the remaining-journey proposal.");
            if (day.Array("stops")!.OfType<JsonObject>().Any(stop => stop.Str("id") == firstStop.Str("id")))
                return new Assessment(false, "The completed activity was still scheduled as a future stop.");
        }
        if (day.Array("stops")!.OfType<JsonObject>().GroupBy(stop => stop.Str("id"), StringComparer.Ordinal)
            .Any(group => group.Key.Length > 0 && group.Count() > 1))
            return new Assessment(false, "The remaining-journey proposal repeated a future stop.");
        foreach (var id in expected.Array("mustExcludeAfterFatigue")?.OfType<JsonValue>().Select(node => node.GetValue<string>()) ?? [])
        {
            if (!originalDay.Array("stops")!.OfType<JsonObject>().Any(stop => stop.Str("id") == id))
                return new Assessment(false, "The fatigue case did not begin with the optional stop it claims to remove: " + id);
            if (day.Array("stops")!.OfType<JsonObject>().Any(stop => stop.Str("id") == id))
                return new Assessment(false, "The fatigue constraint did not remove the optional stop: " + id);
        }
        if (!day.Bool("executionStarted") || day.Int("planningStartMinute") != expected.Int("currentMinute", 640))
            return new Assessment(false, "The replay did not mark the day as an in-progress replanning session at the supplied current time.");
        if (updated.Object("schedule")!.Array("days")![0]!.Int("maxSteps") != 7000)
            return new Assessment(false, "The fatigue request did not apply the 7,000-step cap to the replanned day.");
        if (updated.Object("schedule")!.Array("issues")!.OfType<JsonObject>().Any(issue => issue.Bool("blocking")))
            return new Assessment(false, "The replay returned a proposal with unresolved blocking schedule issues.");
        if (expected.Bool("recordedRoadReplay"))
        {
            var provider = replay.Str("provider");
            var legs = day.Array("legs")!.OfType<JsonObject>().ToArray();
            if (legs.Length == 0 || legs.Any(leg => !leg.Bool("recordedReplay") || !leg.Str("source").Contains(provider, StringComparison.Ordinal) ||
                leg.Str("sourceFixture") != ReplayIdentity(replay)))
                return new Assessment(false, "The replanned route did not retain the recorded-road evidence on every leg.");
            var source = updated.Object("schedule")!.Str("source");
            if (!source.Contains(provider, StringComparison.Ordinal))
                return new Assessment(false, "The replay provenance was mislabeled in the resulting schedule.");
            if (replay.Str("rerouteEvidence") == "composed_historical_path" && !legs.Any(leg => leg.Str("replayKind") == "composed_historical_path" && leg.Array("derivedFrom") is { Count: > 0 }))
                return new Assessment(false, "The reroute did not disclose the composed historical path it needed after removing the optional stop.");
            if (replay.Str("rerouteEvidence") == "direct_provider_recorded_leg" && !legs.Any(leg => leg.Str("replayKind") == "provider_recorded_leg" &&
                leg.Str("replayLegId") == "garden_to_rocks" && leg.Str("source").Contains(provider, StringComparison.Ordinal)))
                return new Assessment(false, "The reroute did not retain the direct provider-recorded garden-to-rocks path.");
        }
        if (!StringComparer.Ordinal.Equals(original.Str("mode"), "demo"))
            return new Assessment(false, "The replay seed unexpectedly changed transport mode.");
        return new Assessment(true, $"Historical {replay.Str("provider")} replay safely rejected missing evidence, then removed the optional Opera House visit to satisfy the fatigue cap while preserving the completed stop.");
    }

    private static bool SameRecordedStop(JsonObject expected, JsonObject actual) =>
        expected.Str("id") == actual.Str("id") && expected.Str("name") == actual.Str("name") &&
        SamePoint(expected, actual) && expected.Str("arrival") == actual.Str("arrival") &&
        expected.Str("departure") == actual.Str("departure") && expected.Int("steps") == actual.Int("steps") &&
        Math.Abs(expected.Number("cost") - actual.Number("cost")) < 0.001;

    private static bool AppliesToStrategy(JsonObject testCase, string strategy) =>
        testCase.Array("strategies") is not JsonArray strategies || strategies.OfType<JsonValue>().Any(node => node.GetValue<string>() == strategy);

    private static string ReplayIdentity(JsonObject replay) => replay.Str("sourceFixture", replay.Str("captureClient", "recorded provider snapshot"));

    private static void ValidateRecordedWalkingReplay(string root, JsonObject replay)
    {
        if (replay.Str("provider").Length == 0 || !replay.Bool("notLiveNavigation"))
            throw new InvalidDataException("Recorded walking replay must declare provider and non-live status.");
        if (!DateOnly.TryParse(replay.Str("recordedDate"), out var recordedDate))
            throw new InvalidDataException("Recorded walking replay must declare a valid recordedDate.");
        var sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, sydney).DateTime);
        if (recordedDate > today) throw new InvalidDataException("Recorded walking replay cannot be dated in the future.");
        var legs = replay.Array("legs")?.OfType<JsonObject>().ToArray() ?? [];
        if (legs.Any(leg => leg.Str("id").Length == 0 || leg.Str("from").Length == 0 || leg.Str("to").Length == 0 ||
            leg.Number("meters") <= 0 || leg.Number("seconds") <= 0) || legs.Select(leg => leg.Str("id")).Distinct(StringComparer.Ordinal).Count() != legs.Length)
            throw new InvalidDataException("Recorded walking replay contains an invalid or duplicate leg.");
        var direct = legs.Where(leg => leg.Str("kind") == "provider_recorded_leg").ToArray();
        var expectedPairs = new[] { (From: "start", To: "garden"), (From: "garden", To: "opera"), (From: "opera", To: "rocks"), (From: "rocks", To: "mca"), (From: "mca", To: "end") };
        if (expectedPairs.Any(pair => direct.Count(leg => leg.Str("from") == pair.From && leg.Str("to") == pair.To) != 1))
            throw new InvalidDataException("Recorded walking replay must retain the five ordered provider legs.");
        var directReroute = direct.SingleOrDefault(leg => leg.Str("from") == "garden" && leg.Str("to") == "rocks");
        if (direct.Length != expectedPairs.Length + (directReroute is null ? 0 : 1))
            throw new InvalidDataException("Recorded walking replay contains an unexpected direct provider leg.");

        var composed = legs.Where(leg => leg.Str("kind") == "composed_historical_path").ToArray();
        var rerouteEvidence = replay.Str("rerouteEvidence");
        if (rerouteEvidence == "direct_provider_recorded_leg")
        {
            if (directReroute is null || composed.Length != 0)
                throw new InvalidDataException("The direct fatigue reroute must include one fresh garden-to-rocks provider leg and no composed substitute.");
        }
        else if (rerouteEvidence == "composed_historical_path")
        {
            if (directReroute is not null || composed.Length != 1 || composed[0].Str("from") != "garden" || composed[0].Str("to") != "rocks")
                throw new InvalidDataException("The composed fatigue reroute must declare exactly one garden-to-rocks path.");
            var componentIds = composed[0].Array("derivedFrom")?.OfType<JsonValue>().Select(node => node.GetValue<string>()).ToArray() ?? [];
            var directById = direct.ToDictionary(leg => leg.Str("id"), StringComparer.Ordinal);
            if (componentIds.Length != 2 || componentIds.Any(id => !directById.ContainsKey(id)))
                throw new InvalidDataException("The composed historical path must identify two recorded source legs.");
            if (Math.Abs(composed[0].Number("meters") - componentIds.Sum(id => directById[id].Number("meters"))) > 0.001 ||
                Math.Abs(composed[0].Number("seconds") - componentIds.Sum(id => directById[id].Number("seconds"))) > 0.001)
                throw new InvalidDataException("The composed historical path must equal the sum of its recorded source legs.");
        }
        else throw new InvalidDataException("Recorded walking replay must declare direct or composed fatigue-reroute evidence.");

        var sourceFixture = replay.Str("sourceFixture");
        if (sourceFixture.Length > 0)
        {
            var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fixturePath = Path.GetFullPath(Path.Combine(root, sourceFixture));
            if (!fixturePath.StartsWith(rootPath, StringComparison.Ordinal) || !File.Exists(fixturePath))
                throw new InvalidDataException("Recorded walking replay source fixture is missing or outside the repository root.");
            var fixture = JsonNode.Parse(File.ReadAllText(fixturePath)) as JsonObject
                ?? throw new InvalidDataException("Recorded walking replay source fixture is not a JSON object.");
            var fixtureSteps = fixture.Array("legs")?.OfType<JsonObject>().Select(leg => leg.Array("steps")?.OfType<JsonObject>().FirstOrDefault()).ToArray() ?? [];
            var originalDirect = expectedPairs.Select(pair => direct.Single(leg => leg.Str("from") == pair.From && leg.Str("to") == pair.To)).ToArray();
            if (fixtureSteps.Length != originalDirect.Length || fixtureSteps.Any(step => step is null))
                throw new InvalidDataException("Recorded walking replay source fixture does not contain the expected walking legs.");
            for (var index = 0; index < originalDirect.Length; index++)
            {
                var fixtureStep = fixtureSteps[index]!;
                if (Math.Abs(originalDirect[index].Number("meters") - fixtureStep.Number("meters")) > 0.001 ||
                    Math.Abs(originalDirect[index].Number("seconds") - fixtureStep.Number("seconds")) > 0.001)
                    throw new InvalidDataException("Recorded walking replay differs from its declared source fixture.");
            }
        }
        else ValidateMapKitCapture(replay, direct, recordedDate, sydney);
    }

    private static void ValidateMapKitCapture(JsonObject replay, IReadOnlyList<JsonObject> direct, DateOnly recordedDate, TimeZoneInfo sydney)
    {
        if (replay.Str("provider") != "Apple Maps / MapKit" || !replay.Str("captureClient").Contains("MapKit", StringComparison.Ordinal) ||
            replay.Str("requestTransportType") != "walking" || replay.Str("timeZone") != "Australia/Sydney" || replay["usesDeviceLocation"] is null || replay.Bool("usesDeviceLocation"))
            throw new InvalidDataException("A self-contained replay must be an explicit non-location Apple MapKit walking capture.");
        if (!DateTimeOffset.TryParse(replay.Str("capturedAt"), out var replayCapturedAt))
            throw new InvalidDataException("Apple MapKit replay is missing a parseable capture timestamp.");
        var captureDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(replayCapturedAt, sydney).DateTime);
        if (captureDate != recordedDate) throw new InvalidDataException("Apple MapKit replay date differs from its capture timestamp.");
        var expectedPoints = new Dictionary<string, (double Lat, double Lng)>(StringComparer.Ordinal)
        {
            ["start"] = (-33.8611, 151.2126), ["end"] = (-33.8611, 151.2126),
            ["garden"] = (-33.8642, 151.2166), ["opera"] = (-33.8568, 151.2153),
            ["rocks"] = (-33.8599, 151.2090), ["mca"] = (-33.8599, 151.2091)
        };
        foreach (var leg in direct)
        {
            if (!DateTimeOffset.TryParse(leg.Str("capturedAt"), out var legCapturedAt))
                throw new InvalidDataException("Apple MapKit replay leg is missing a parseable capture timestamp.");
            if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(legCapturedAt, sydney).DateTime) != recordedDate)
                throw new InvalidDataException("Apple MapKit replay leg date differs from the manifest.");
            if (!expectedPoints.TryGetValue(leg.Str("from"), out var from) || !expectedPoints.TryGetValue(leg.Str("to"), out var to) ||
                leg.Object("fromCoordinate") is not { } fromPoint || leg.Object("toCoordinate") is not { } toPoint ||
                Math.Abs(fromPoint.Number("lat") - from.Lat) > 0.000001 || Math.Abs(fromPoint.Number("lng") - from.Lng) > 0.000001 ||
                Math.Abs(toPoint.Number("lat") - to.Lat) > 0.000001 || Math.Abs(toPoint.Number("lng") - to.Lng) > 0.000001)
                throw new InvalidDataException("Apple MapKit replay leg endpoints do not match the fixed public landmark manifest.");
        }
    }

    private static Assessment AssessSuccess(JsonObject trip, JsonObject expected)
    {
        if (expected.Str("outcome") == "error") return new Assessment(false, "Expected an error but the planner returned a draft.");
        var stops = trip.Array("days")!.OfType<JsonObject>().SelectMany(day => day.Array("stops")!.OfType<JsonObject>()).ToArray();
        var ids = stops.Select(stop => stop.Str("id")).ToHashSet(StringComparer.Ordinal);
        var missed = expected.Array("mustInclude")?.Select(x => x!.GetValue<string>()).Where(id => !ids.Contains(id)).ToArray() ?? [];
        var forbidden = expected.Array("mustExclude")?.Select(x => x!.GetValue<string>()).Where(ids.Contains).ToArray() ?? [];
        if (missed.Length > 0) return new Assessment(false, "Missing required stops: " + string.Join(", ", missed));
        if (forbidden.Length > 0) return new Assessment(false, "Included excluded stops: " + string.Join(", ", forbidden));
        if (expected.Bool("indoorOnly") && stops.Any(stop => !stop.Bool("indoor") && !expected.Array("mustInclude")!.Any(x => x!.GetValue<string>() == stop.Str("id"))))
            return new Assessment(false, "Rain case included an optional outdoor stop.");
        if (expected["maxCost"] is not null && trip.Array("days")!.OfType<JsonObject>().Any(day => day.Number("estimatedCost") > expected.Number("maxCost") + 0.001))
            return new Assessment(false, "A daily estimate exceeds the configured cost ceiling.");
        if (expected["maxSteps"] is not null && trip.Array("days")!.OfType<JsonObject>().Any(day => day.Int("estimatedSteps") > expected.Int("maxSteps")))
            return new Assessment(false, "A daily estimate exceeds the configured step ceiling.");
        if (expected["minimumFreeDays"] is not null && trip.Array("days")!.OfType<JsonObject>().Count(day => day.Bool("freeTime")) < expected.Int("minimumFreeDays"))
            return new Assessment(false, "Candidate shortage was not surfaced as free time.");
        if (expected["minimumMealStops"] is not null && stops.Count(stop => stop.Str("activityType") == "meal") < expected.Int("minimumMealStops"))
            return new Assessment(false, "The food-focused request did not schedule a concrete meal stop.");
        if (expected.Bool("uniqueStops") && stops.GroupBy(stop => stop.Str("id"), StringComparer.Ordinal).Any(group => group.Key.Length > 0 && group.Count() > 1))
            return new Assessment(false, "A place was repeated despite the no-repeat requirement.");
        if (expected.Bool("endsAtRequestedDestination"))
        {
            var destination = trip.Object("profile")!.Object("end")!;
            if (trip.Array("days")!.OfType<JsonObject>().Any(day => !SamePoint(day.Object("end")!, destination)))
                return new Assessment(false, "A day did not end at the requested destination.");
        }
        return new Assessment(true, "All declared expectations were met.");
    }

    private static bool SamePoint(JsonObject a, JsonObject b) =>
        Math.Abs(a.Number("lat") - b.Number("lat")) < 0.000001 && Math.Abs(a.Number("lng") - b.Number("lng")) < 0.000001;

    private static void ValidateHabits(string habitsPath, JsonArray cases)
    {
        var rows = File.ReadAllLines(habitsPath, Encoding.UTF8);
        if (rows.Length < 2) throw new InvalidDataException("user-habits.csv must contain a header and data rows.");
        var header = rows[0].Split(',');
        if (!RequiredCaseColumns.All(column => header.Contains(column, StringComparer.Ordinal)))
            throw new InvalidDataException("user-habits.csv is missing a required column.");
        var ids = rows.Skip(1).Select(row => row.Split(',')[0]).ToHashSet(StringComparer.Ordinal);
        var missing = cases.OfType<JsonObject>().Select(item => item.Str("id")).Where(id => !ids.Contains(id)).ToArray();
        if (missing.Length > 0) throw new InvalidDataException("user-habits.csv is missing cases: " + string.Join(", ", missing));
    }

    private static void ValidateExpectationChecks(string expectationsPath, JsonArray cases)
    {
        var rows = File.ReadAllLines(expectationsPath, Encoding.UTF8);
        if (rows.Length < 2) throw new InvalidDataException("expected-checks.csv must contain a header and data rows.");
        var header = rows[0].Split(',');
        if (!RequiredExpectationColumns.All(column => header.Contains(column, StringComparer.Ordinal)))
            throw new InvalidDataException("expected-checks.csv is missing a required column.");
        var caseIdIndex = Array.IndexOf(header, "case_id");
        var outcomeIndex = Array.IndexOf(header, "outcome");
        var expectedOutcomes = rows.Skip(1)
            .Select(row => row.Split(','))
            .ToDictionary(columns => columns[caseIdIndex], columns => columns[outcomeIndex], StringComparer.Ordinal);
        var caseIds = cases.OfType<JsonObject>().Select(item => item.Str("id")).ToHashSet(StringComparer.Ordinal);
        if (!caseIds.SetEquals(expectedOutcomes.Keys))
            throw new InvalidDataException("expected-checks.csv must contain one row for each evaluation case.");
        foreach (var testCase in cases.OfType<JsonObject>())
        {
            var expectedOutcome = testCase.Object("expected")!.Str("outcome");
            if (!StringComparer.Ordinal.Equals(expectedOutcome, expectedOutcomes[testCase.Str("id")]))
                throw new InvalidDataException($"expected-checks.csv outcome does not match {testCase.Str("id")}.");
        }
    }

    private static void WriteCatalogSnapshot(string output, IReadOnlyList<JsonObject> catalog)
    {
        var rows = new StringBuilder("id,name,latitude,longitude,category,activity_type,visit_minutes,cost,opening_status,source\n");
        foreach (var place in catalog)
        {
            rows.AppendLine(string.Join(',', new[]
            {
                Csv(place.Str("id")), Csv(place.Str("name")), Csv(place.Number("lat").ToString("F6", CultureInfo.InvariantCulture)),
                Csv(place.Number("lng").ToString("F6", CultureInfo.InvariantCulture)), Csv(place.Str("category")), Csv(place.Str("activityType", "attraction")),
                Csv(place.Int("visitMinutes").ToString(CultureInfo.InvariantCulture)), Csv(place.Number("cost").ToString("0.##", CultureInfo.InvariantCulture)),
                Csv(place["periods"] is null ? "unverified demo window" : "regular periods"), Csv("server/places.json")
            }));
        }
        File.WriteAllText(Path.Combine(output, "catalog-snapshot.csv"), rows.ToString(), new UTF8Encoding(false));
    }

    private static void WriteCaseArtifact(string output, CaseResult result)
    {
        var document = new JsonObject
        {
            ["case"] = result.TestCase.DeepClone(), ["strategy"] = result.Strategy, ["status"] = result.Status,
            ["passed"] = result.Passed, ["summary"] = result.Summary, ["error"] = result.Error,
            ["output"] = result.Output?.DeepClone()
        };
        File.WriteAllText(Path.Combine(output, "cases", result.Id + "-" + result.Strategy + ".json"),
            document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private static async Task WriteJsonAsync(string path, JsonObject document) =>
        await File.WriteAllTextAsync(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

    private static string RenderCsv(IEnumerable<CaseResult> results)
    {
        var rows = new StringBuilder("case_id,title,strategy,status,passed,scenario,days,stops,estimated_steps,estimated_cost,replay_pre_fatigue_steps,replay_removed_stops,warnings,unverified,trace_sources,summary,error\n");
        foreach (var result in results)
        {
            var days = result.Output?.Array("days")?.OfType<JsonObject>().ToArray() ?? [];
            var warnings = result.Output?.Array("warnings")?.OfType<JsonValue>().Select(node => node.GetValue<string>()).ToArray() ?? [];
            var dayWarnings = days.SelectMany(day => day.Array("warnings")?.OfType<JsonValue>().Select(node => node.GetValue<string>()) ?? []);
            var allWarnings = warnings.Concat(dayWarnings).Distinct().ToArray();
            var trace = (result.Output?.Array("trace")?.OfType<JsonObject>().Select(item => item.Str("source")) ?? [])
                .Concat(result.Output?.Array("walkingRoutes")?.OfType<JsonObject>().Select(item => item.Str("source")) ?? [])
                .Where(source => source.Length > 0).Distinct().ToArray();
            var replay = result.Output?.Object("evaluationReplay");
            var initialStops = replay?.Array("initialStopIds")?.OfType<JsonValue>().Select(node => node.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
            var remainingStops = replay?.Array("remainingStopIds")?.OfType<JsonValue>().Select(node => node.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
            var removedReplayStops = initialStops.Except(remainingStops).ToArray();
            var unverified = allWarnings.Where(text => text.Contains("未核实") || text.Contains("估算") || text.Contains("未知")).ToArray();
            rows.AppendLine(string.Join(',', new[]
            {
                Csv(result.Id), Csv(result.Title), Csv(result.Strategy), Csv(result.Status), Csv(result.Passed ? "true" : "false"), Csv(result.TestCase.Str("scenario", "initial")),
                Csv(days.Length.ToString(CultureInfo.InvariantCulture)), Csv(days.Sum(day => day.Array("stops")?.Count ?? 0).ToString(CultureInfo.InvariantCulture)),
                Csv(days.Sum(day => day.Int("estimatedSteps")).ToString(CultureInfo.InvariantCulture)), Csv(days.Sum(day => day.Number("estimatedCost")).ToString("0.##", CultureInfo.InvariantCulture)),
                Csv(replay?.Int("preFatigueSteps").ToString(CultureInfo.InvariantCulture) ?? ""), Csv(string.Join(" | ", removedReplayStops)),
                Csv(string.Join(" | ", allWarnings)), Csv(string.Join(" | ", unverified)), Csv(string.Join(" | ", trace)), Csv(result.Summary), Csv(result.Error ?? "")
            }));
        }
        return rows.ToString();
    }

    private static string RenderHtml(IReadOnlyList<CaseResult> results, bool aiConfigured, JsonObject replay, TransitReplayResult transitReplay)
    {
        var local = results.Where(result => result.Strategy == "local_rule").ToArray();
        var rows = new StringBuilder();
        foreach (var result in results)
        {
            var days = result.Output?.Array("days")?.OfType<JsonObject>().ToArray() ?? [];
            var profile = result.TestCase.Object("profile")!;
            var expected = result.TestCase.Object("expected")!;
            var mustVisit = string.Join("、", profile.Array("mustVisit")?.OfType<JsonValue>().Select(node => node.GetValue<string>()) ?? []);
            var requirements = new[]
            {
                profile.Str("request"),
                $"预算 A${profile.Number("budget"):0.##} / {profile.Int("maxSteps")} 步 / {profile.Int("days")} 天",
                $"必去：{(string.IsNullOrWhiteSpace(mustVisit) ? profile.Str("mustDo", "无") : mustVisit)}；避开：{profile.Str("avoid", "无")}",
                $"检查：{RenderExpectation(expected)}"
            };
            var sequence = string.Join("\n", days.Select(day =>
            {
                var stops = day.Array("stops")?.OfType<JsonObject>() ?? [];
                var orderedStops = stops.Select(stop => $"{stop.Str("arrival")}–{stop.Str("departure")} {stop.Str("name")}");
                return $"{day.Str("date")}: {day.Object("start")?.Str("name")} → {string.Join(" → ", orderedStops)} → {day.Object("end")?.Str("name")}";
            }));
            var estimates = string.Join("<br>", days.Select(day =>
            {
                var transportMinutes = (day.Array("legs")?.OfType<JsonObject>() ?? []).Sum(leg => leg.Int("minutes"));
                var transportSource = (day.Array("legs")?.OfType<JsonObject>() ?? []).FirstOrDefault()?.Str("source", "未提供");
                return $"{day.Str("date")}: 结束 {day.Str("finishTime")}；步数 {day.Int("estimatedSteps")}；花费 A${day.Number("estimatedCost"):0.##}；交通 {transportMinutes} 分钟（{transportSource}）";
            }));
            var evaluationReplay = result.Output?.Object("evaluationReplay");
            if (evaluationReplay is not null)
            {
                var initial = evaluationReplay.Array("initialStopIds")?.OfType<JsonValue>().Select(node => node.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
                var remaining = evaluationReplay.Array("remainingStopIds")?.OfType<JsonValue>().Select(node => node.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
                var removed = initial.Except(remaining).Select(DisplayCasePlace).ToArray();
                var replayLine = $"评测回放：疲劳前 {evaluationReplay.Int("preFatigueSteps")} 步 → 疲劳后 {evaluationReplay.Int("postFatigueSteps")} 步；移除 {string.Join("、", removed)}";
                estimates = string.IsNullOrWhiteSpace(estimates) ? replayLine : estimates + "<br>" + replayLine;
            }
            var warnings = (result.Output?.Array("warnings")?.OfType<JsonValue>().Select(node => node.GetValue<string>()) ?? [])
                .Concat(days.SelectMany(day => day.Array("warnings")?.OfType<JsonValue>().Select(node => node.GetValue<string>()) ?? []))
                .Distinct()
                .ToArray();
            var issue = result.Error is null ? string.Join("<br>", warnings.Select(Html)) : Html(result.Error);
            var status = result.Status switch
            {
                "passed" => "通过",
                "expected_rejection" => "符合预期：明确拒绝",
                "not_run" => "未运行",
                "not_applicable" => "不适用",
                _ => "未通过"
            };
            var statusClass = result.Status switch
            {
                "passed" => "pass",
                "expected_rejection" => "expected",
                "not_run" => "skip",
                "not_applicable" => "skip",
                _ => "fail"
            };
            rows.Append("<tr><td>").Append(Html(result.Id)).Append("<br><strong>").Append(Html(result.Title)).Append("</strong></td><td>")
                .Append(string.Join("<br>", requirements.Select(Html))).Append("</td><td>").Append(Html(result.Strategy)).Append("</td><td class=\"")
                .Append(statusClass).Append("\">").Append(status)
                .Append("<br><span class=\"detail\">").Append(Html(result.Summary)).Append("</span></td><td>")
                .Append(string.IsNullOrWhiteSpace(sequence) ? "—" : Html(sequence).Replace("\n", "<br>")).Append("</td><td>")
                .Append(string.IsNullOrWhiteSpace(estimates) ? "—" : Html(estimates).Replace("&lt;br&gt;", "<br>")).Append("</td><td>")
                .Append(string.IsNullOrWhiteSpace(issue) ? "—" : issue).AppendLine("</td></tr>");
        }
        var localRoutes = local.Count(result => result.Status == "passed");
        var localExpectedRejections = local.Count(result => result.Status == "expected_rejection");
        var localFailures = local.Count(result => result.Status == "failed");
        var aiResults = results.Where(result => result.Strategy == "ai_plus_rules" && result.Status != "not_applicable").ToArray();
        var aiExecuted = aiResults.Count(result => result.Output?.Array("trace")?.OfType<JsonObject>()
            .Any(trace => trace.Str("source") == "NVIDIA NIM") == true);
        var rejectedBeforeModel = aiResults.Count(result => result.Status == "expected_rejection" &&
            result.Output?.Array("trace")?.OfType<JsonObject>().Any(trace => trace.Str("source") == "NVIDIA NIM") != true);
        var aiNote = aiConfigured
            ? $"已真实调用 NVIDIA：{aiExecuted}/{aiResults.Length} 个适用案例到达模型；{rejectedBeforeModel} 个案例先被确定性规则按预期拒绝。逐案查看状态。"
            : "未运行；报告未将任何模拟结果称为 AI 结果。";
        var replayEvidence = replay.Str("rerouteEvidence") == "direct_provider_recorded_leg"
            ? "疲劳后使用一段独立录制的植物园→岩石区路段"
            : "疲劳后使用由两段原始路段透明拼接的植物园→岩石区连接段";
        var transitNote = transitReplay.Passed
            ? $"T01 已用固定的 {transitReplay.Artifact.Str("provider")} 历史快照验证 Central Station→Circular Quay 的公共交通步骤、时刻连续性和排程器实际使用该路段；它不是实时班次、实时导航或票价验证。"
            : "T01 TfNSW 固定公交回放未通过；本轮不能把公共交通算法称为已验证。";
        var replayNote = $"E08 仅使用有日期和来源的 {replay.Str("provider")} 历史道路回放（录制日期 {replay.Str("recordedDate")}）；{replayEvidence}。它验证途中重算的证据门槛、已完成活动保留和疲劳步数导致的可选站点移除；它不是实时导航。{transitNote} 其余路线仍是本地演示估算；只有实际执行的 AI 结果才能被称为模型策略验证。";
        return "<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "<title>SydneyBuddy 推荐与路线评测</title><style>body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;margin:32px;color:#173642;background:#f6fbfa}main{max-width:1800px;margin:auto}h1{margin-bottom:4px}.note{background:#e6f3f0;padding:14px;border-radius:10px}table{width:100%;border-collapse:collapse;background:#fff;margin-top:20px;font-size:13px}th,td{text-align:left;vertical-align:top;padding:10px;border-bottom:1px solid #d9e4e1;line-height:1.5}th{background:#0f6d69;color:#fff;position:sticky;top:0}.pass{color:#167242;font-weight:700}.expected,.skip{color:#8a5a00;font-weight:700}.fail{color:#b42318;font-weight:700}.detail{font-weight:400;color:#35545b}code{font-size:12px}</style>"
            + "<main><h1>推荐与路线算法独立评测</h1><p>本地：" + localRoutes.ToString(CultureInfo.InvariantCulture) + " 个路线案例通过；" + localExpectedRejections.ToString(CultureInfo.InvariantCulture) + " 个案例按预期明确拒绝；" + localFailures.ToString(CultureInfo.InvariantCulture) + " 个真实失败。AI + 规则：" + Html(aiNote) + "</p>"
            + "<p class=\"note\">固定输入：<code>evaluation/data/cases.json</code>。地点快照、案例原始输出、CSV 与问题报告均保存在本次输出目录。除 E08 外，默认路线为本地演示估算，非道路导航；" + Html(replayNote) + "</p>"
            + "<table><thead><tr><th>案例</th><th>需求与约束</th><th>策略</th><th>检查结果</th><th>地点顺序（抵达–离开）</th><th>时间、步数、花费与交通估计</th><th>未核实项 / 问题</th></tr></thead><tbody>" + rows + "</tbody></table></main></html>";
    }

    private static string RenderExpectation(JsonObject expected)
    {
        var parts = new List<string> { expected.Str("outcome") == "error" ? "应明确报错" : "应生成可行路线" };
        if (expected["maxCost"] is not null) parts.Add($"花费 ≤ A${expected.Number("maxCost"):0.##}");
        if (expected["maxSteps"] is not null) parts.Add($"步数 ≤ {expected.Int("maxSteps")}");
        if (expected.Array("mustInclude") is { Count: > 0 } include) parts.Add("包含 " + string.Join("、", include.OfType<JsonValue>().Select(node => node.GetValue<string>())));
        if (expected.Array("mustExclude") is { Count: > 0 } exclude) parts.Add("不含 " + string.Join("、", exclude.OfType<JsonValue>().Select(node => node.GetValue<string>())));
        if (expected.Bool("indoorOnly")) parts.Add("雨天活动均为室内");
        if (expected["minimumFreeDays"] is not null) parts.Add($"至少 {expected.Int("minimumFreeDays")} 天明确留白");
        if (expected["minimumMealStops"] is not null) parts.Add($"至少 {expected.Int("minimumMealStops")} 个具体用餐地点");
        if (expected.Bool("uniqueStops")) parts.Add("地点不重复");
        if (expected.Bool("endsAtRequestedDestination")) parts.Add("抵达指定终点");
        if (expected.Bool("preserveCompleted")) parts.Add("保留已完成活动");
        if (expected.Bool("recordedRoadReplay")) parts.Add("仅使用注明来源的历史道路回放");
        if (expected.Array("mustExcludeAfterFatigue") is { Count: > 0 } fatigueExcludes) parts.Add("疲劳后移除 " + string.Join("、", fatigueExcludes.OfType<JsonValue>().Select(node => DisplayCasePlace(node.GetValue<string>()))));
        if (expected.Bool("requiresRecordedRerouteEvidence")) parts.Add("保留声明的疲劳后道路证据");
        return string.Join("；", parts);
    }

    private static string DisplayCasePlace(string id) => id switch
    {
        "opera" => "歌剧院",
        _ => id
    };

    private static string RenderIssues(IReadOnlyList<CaseResult> results, JsonObject replay, TransitReplayResult transitReplay)
    {
        var issueCases = results.Where(result => result.Status == "failed").Take(3).ToArray();
        var report = new StringBuilder("# 实际失败的评测发现\n\n");
        if (issueCases.Length == 0 && !transitReplay.Passed)
        {
            report.Append("## T01 - TfNSW 固定公交回放\n\n")
                .Append("- 对应模块：`TransportEvidence` / `ScheduleEditorService`\n")
                .Append("- 实际输出：").Append(transitReplay.Summary).Append("\n")
                .Append("- 期望行为：固定 TfNSW 快照必须含真实公共交通步骤，并被排程器按相同起终点、日期和分钟消费。\n")
                .Append("- 评测结论：这是实际公交回放失败，不能宣称 TfNSW 已验证。\n");
            return report.ToString();
        }
        if (issueCases.Length == 0)
        {
            var expectedRejections = results.Where(result => result.Status == "expected_rejection")
                .Select(result => result.Id).Distinct(StringComparer.Ordinal).ToArray();
            report.Append("本次已执行策略未出现实际失败。");
            if (expectedRejections.Length > 0)
                report.Append(string.Join("、", expectedRejections)).Append(" 是符合预期的安全拒绝，不应被列为缺陷。");
            report.Append("\n\n");
            report.Append("## 覆盖边界\n\n");
            report.Append(results.Any(result => result.Status == "not_run")
                ? "AI + 规则未运行；没有模拟模型结果。"
                : "AI + 规则已按本轮配置执行；结果仍应结合每案输出和模型非确定性解读。");
            var reroute = replay.Str("rerouteEvidence") == "direct_provider_recorded_leg" ? "一段独立录制的植物园→岩石区路线" : "一段明确标为原始路段拼接的连接";
            report.Append("E08 已用固定 ").Append(replay.Str("provider")).Append(" 道路回放验证疲劳后的成功重算、已完成活动保留、可选站点移除及缺证据时拒绝；疲劳后使用 ").Append(reroute).Append("。它不是实时导航。T01 已通过固定 TfNSW 公交快照的步骤、时刻和排程消费验证；它仍不是实时班次、票价或全图公交验证。其余路线仍是本地演示估算；未执行的模型策略不能被称为已验证。\n");
            return report.ToString();
        }
        foreach (var result in issueCases)
        {
            report.Append("## ").Append(result.Id).Append(" - ").Append(result.Title).Append("\n\n")
                .Append("- 对应模块：`").Append(result.TestCase.Str("module")).Append("`\n")
                .Append("- 输入：`").Append(result.TestCase.Object("profile")!.ToJsonString()).Append("`\n")
                .Append("- 实际输出：").Append(result.Error ?? result.Summary).Append("\n")
                .Append("- 期望行为：").Append(result.TestCase.Object("expected")!.Str("expectedBehavior", RenderExpectation(result.TestCase.Object("expected")!))).Append("\n")
                .Append("- 评测结论：这是实际断言失败，需要修复或调整预期后再提交。\n\n");
        }
        if (!transitReplay.Passed)
            report.Append("## T01 - TfNSW 固定公交回放\n\n- 对应模块：`TransportEvidence` / `ScheduleEditorService`\n- 实际输出：")
                .Append(transitReplay.Summary).Append("\n- 期望行为：固定 TfNSW 快照必须被排程器消费。\n\n");
        return report.ToString();
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private sealed record Assessment(bool Passed, string Summary);

    private sealed record CaseResult(JsonObject TestCase, string Strategy, string Status, bool Passed, string Summary, JsonObject? Output, string? Error)
    {
        public string Id => TestCase.Str("id");
        public string Title => TestCase.Str("title");
        public static CaseResult NotRun(JsonObject testCase, string strategy, string reason) => new(testCase, strategy, "not_run", false, reason, null, reason);
        public static CaseResult NotApplicable(JsonObject testCase, string strategy, string reason) => new(testCase, strategy, "not_applicable", false, reason, null, null);
    }

    private sealed class EmptyMemoryService : IMemoryService
    {
        public Task<JsonObject> RunAsync(string action, JsonObject arguments, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new JsonObject { ["memories"] = new JsonArray(), ["context"] = "" });
        }
    }

    private sealed class BlockedExternalClient : IExternalJsonClient
    {
        public Task<JsonObject> FetchAsync(string url, JsonNode? payload = null, IReadOnlyDictionary<string, string>? headers = null,
            int timeoutSeconds = 25, CancellationToken cancellationToken = default) =>
            Task.FromException<JsonObject>(new ServiceException("评测默认禁止外部请求；此策略只能运行本地确定性规则。"));
    }

    private sealed record Options(string? Root, string? Output, bool WithAi, string? WalkingReplay,
        bool CaptureTfNswReplay, DateOnly? TransitDate, int? TransitMinute)
    {
        public static Options Parse(IReadOnlyList<string> args)
        {
            string? root = null;
            string? output = null;
            string? walkingReplay = null;
            var withAi = false;
            var captureTfNswReplay = false;
            DateOnly? transitDate = null;
            int? transitMinute = null;
            for (var index = 0; index < args.Count; index++)
            {
                switch (args[index])
                {
                    case "--root" when index + 1 < args.Count: root = Path.GetFullPath(args[++index]); break;
                    case "--output" when index + 1 < args.Count: output = Path.GetFullPath(args[++index]); break;
                    case "--walking-replay" when index + 1 < args.Count: walkingReplay = Path.GetFullPath(args[++index]); break;
                    case "--with-ai": withAi = true; break;
                    case "--capture-tfnsw-replay": captureTfNswReplay = true; break;
                    case "--transit-date" when index + 1 < args.Count:
                        if (!DateOnly.TryParseExact(args[++index], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                            throw new ArgumentException("--transit-date must use yyyy-MM-dd.");
                        transitDate = date;
                        break;
                    case "--transit-minute" when index + 1 < args.Count:
                        if (!int.TryParse(args[++index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute))
                            throw new ArgumentException("--transit-minute must be an integer from 0 to 1439.");
                        transitMinute = minute;
                        break;
                    default: throw new ArgumentException("Usage: dotnet run --project server-dotnet/SydneyBuddy.Evaluation -- [--root path] [--output path] [--walking-replay path] [--with-ai] [--capture-tfnsw-replay [--transit-date yyyy-MM-dd] [--transit-minute 0..1439]]");
                }
            }
            if ((transitDate is not null || transitMinute is not null) && !captureTfNswReplay)
                throw new ArgumentException("--transit-date and --transit-minute require --capture-tfnsw-replay.");
            return new Options(root, output, withAi, walkingReplay, captureTfNswReplay, transitDate, transitMinute);
        }
    }
}
