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
        var cases = JsonNode.Parse(await File.ReadAllTextAsync(casesPath)) as JsonArray
            ?? throw new InvalidDataException("evaluation/data/cases.json must be a JSON array.");
        if (cases.Count != 10) throw new InvalidDataException("The evaluation suite must contain exactly 10 cases.");
        ValidateHabits(habitsPath, cases);
        ValidateExpectationChecks(expectationsPath, cases);
        var transportFixture = JsonNode.Parse(await File.ReadAllTextAsync(transportPath)) as JsonObject
            ?? throw new InvalidDataException("evaluation/data/transport-evidence.json must be a JSON object.");

        var output = options.Output ?? Path.Combine(root, "evaluation", "output",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "cases"));

        using var catalogCache = new CatalogCache();
        var paths = new ProjectPaths(root);
        var secrets = new SecretStore(paths);
        var catalog = new PlaceCatalog(catalogCache, paths);
        var localPlanner = CreatePlanner(catalog, paths, new BlockedExternalClient());
        var aiConfigured = options.WithAi && !string.IsNullOrWhiteSpace(secrets.Read()["NVIDIA_API_KEY"]);
        using var aiHttp = aiConfigured ? new HttpClient() : null;
        var aiPlanner = aiConfigured
            ? CreatePlanner(catalog, paths, new ExternalJsonClient(aiHttp!))
            : null;
        var editor = new ScheduleEditorService(catalog);

        var results = new List<CaseResult>();
        foreach (var node in cases)
        {
            var testCase = node as JsonObject ?? throw new InvalidDataException("Each evaluation case must be an object.");
            var local = await RunCaseAsync(testCase, "local_rule", localPlanner, editor, catalog);
            results.Add(local);
            WriteCaseArtifact(output, local);

            var ai = aiPlanner is null
                ? CaseResult.NotRun(testCase, "ai_plus_rules", options.WithAi
                    ? "NVIDIA_API_KEY is not configured; the AI comparison was not run."
                    : "AI comparison disabled. Re-run with --with-ai after configuring NVIDIA_API_KEY.")
                : await RunCaseAsync(testCase, "ai_plus_rules", aiPlanner, editor, catalog);
            results.Add(ai);
            WriteCaseArtifact(output, ai);
        }

        WriteCatalogSnapshot(output, catalog.Get());
        await WriteJsonAsync(Path.Combine(output, "run-metadata.json"), new JsonObject
        {
            ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["caseCount"] = cases.Count,
            ["localStrategy"] = "demo mode: current local recommendation and deterministic scheduling",
            ["aiStrategy"] = aiConfigured
                ? "assisted mode: current NVIDIA recommendation followed by the same deterministic scheduler"
                : "not run; no model result is substituted or simulated",
            ["transportFixture"] = transportFixture["mode"]?.DeepClone(),
            ["transportFixtureNote"] = transportFixture["note"]?.DeepClone()
        });
        await File.WriteAllTextAsync(Path.Combine(output, "results.csv"), RenderCsv(results), new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(output, "report.html"), RenderHtml(results, aiConfigured), new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(output, "issues.md"), RenderIssues(results), new UTF8Encoding(false));

        var localFailures = results.Where(x => x.Strategy == "local_rule" && !x.Passed).ToArray();
        Console.WriteLine($"Evaluation report written to {output}");
        Console.WriteLine($"Local rule cases: {results.Count(x => x.Strategy == "local_rule" && x.Passed)}/10 passed.");
        Console.WriteLine(aiConfigured
            ? $"AI + rule cases: {results.Count(x => x.Strategy == "ai_plus_rules" && x.Passed)}/10 passed."
            : "AI + rule cases: not run (no model result was fabricated).");
        return localFailures.Length == 0 ? 0 : 1;
    }

    private static Planner CreatePlanner(PlaceCatalog catalog, ProjectPaths paths, IExternalJsonClient external) => new(
        new PlaceService(external, catalog),
        new RecommendationService(external, new EmptyMemoryService()),
        new RouteScheduler(new RouteService(external)),
        new WeatherService(external),
        new SecretStore(paths));

    private static async Task<CaseResult> RunCaseAsync(JsonObject testCase, string strategy, Planner planner,
        ScheduleEditorService editor, PlaceCatalog catalog)
    {
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
            return new CaseResult(testCase, strategy, "completed", assessment.Passed, assessment.Summary, trip, null);
        }
        catch (ServiceException error)
        {
            var expectedError = expected.Str("outcome") == "error" && error.Message.Contains(expected.Str("messageContains"), StringComparison.Ordinal);
            return new CaseResult(testCase, strategy, "error", expectedError,
                expectedError ? "Expected constraint or evidence failure." : "Unexpected service failure.", null, error.Message);
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
        return new Assessment(true, "All declared expectations were met.");
    }

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
        var rows = new StringBuilder("case_id,title,strategy,status,passed,scenario,days,stops,estimated_steps,estimated_cost,warnings,unverified,trace_sources,summary,error\n");
        foreach (var result in results)
        {
            var days = result.Output?.Array("days")?.OfType<JsonObject>().ToArray() ?? [];
            var warnings = result.Output?.Array("warnings")?.OfType<JsonValue>().Select(node => node.GetValue<string>()).ToArray() ?? [];
            var dayWarnings = days.SelectMany(day => day.Array("warnings")?.OfType<JsonValue>().Select(node => node.GetValue<string>()) ?? []);
            var allWarnings = warnings.Concat(dayWarnings).Distinct().ToArray();
            var trace = result.Output?.Array("trace")?.OfType<JsonObject>().Select(item => item.Str("source")).Distinct().ToArray() ?? [];
            var unverified = allWarnings.Where(text => text.Contains("未核实") || text.Contains("估算") || text.Contains("未知")).ToArray();
            rows.AppendLine(string.Join(',', new[]
            {
                Csv(result.Id), Csv(result.Title), Csv(result.Strategy), Csv(result.Status), Csv(result.Passed ? "true" : "false"), Csv(result.TestCase.Str("scenario", "initial")),
                Csv(days.Length.ToString(CultureInfo.InvariantCulture)), Csv(days.Sum(day => day.Array("stops")?.Count ?? 0).ToString(CultureInfo.InvariantCulture)),
                Csv(days.Sum(day => day.Int("estimatedSteps")).ToString(CultureInfo.InvariantCulture)), Csv(days.Sum(day => day.Number("estimatedCost")).ToString("0.##", CultureInfo.InvariantCulture)),
                Csv(string.Join(" | ", allWarnings)), Csv(string.Join(" | ", unverified)), Csv(string.Join(" | ", trace)), Csv(result.Summary), Csv(result.Error ?? "")
            }));
        }
        return rows.ToString();
    }

    private static string RenderHtml(IReadOnlyList<CaseResult> results, bool aiConfigured)
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
            var warnings = (result.Output?.Array("warnings")?.OfType<JsonValue>().Select(node => node.GetValue<string>()) ?? [])
                .Concat(days.SelectMany(day => day.Array("warnings")?.OfType<JsonValue>().Select(node => node.GetValue<string>()) ?? []))
                .Distinct()
                .ToArray();
            var issue = result.Error is null ? string.Join("<br>", warnings.Select(Html)) : Html(result.Error);
            var status = result.Passed ? "通过" : result.Status == "not_run" ? "未运行" : "未通过";
            rows.Append("<tr><td>").Append(Html(result.Id)).Append("<br><strong>").Append(Html(result.Title)).Append("</strong></td><td>")
                .Append(string.Join("<br>", requirements.Select(Html))).Append("</td><td>").Append(Html(result.Strategy)).Append("</td><td class=\"")
                .Append(result.Passed ? "pass" : result.Status == "not_run" ? "skip" : "fail").Append("\">").Append(status)
                .Append("<br><span class=\"detail\">").Append(Html(result.Summary)).Append("</span></td><td>")
                .Append(string.IsNullOrWhiteSpace(sequence) ? "—" : Html(sequence).Replace("\n", "<br>")).Append("</td><td>")
                .Append(string.IsNullOrWhiteSpace(estimates) ? "—" : Html(estimates).Replace("&lt;br&gt;", "<br>")).Append("</td><td>")
                .Append(string.IsNullOrWhiteSpace(issue) ? "—" : issue).AppendLine("</td></tr>");
        }
        var aiNote = aiConfigured ? "已尝试真实 NVIDIA 调用；逐案查看状态。" : "未运行；报告未将任何模拟结果称为 AI 结果。";
        return "<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + "<title>SydneyBuddy 推荐与路线评测</title><style>body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;margin:32px;color:#173642;background:#f6fbfa}main{max-width:1800px;margin:auto}h1{margin-bottom:4px}.note{background:#e6f3f0;padding:14px;border-radius:10px}table{width:100%;border-collapse:collapse;background:#fff;margin-top:20px;font-size:13px}th,td{text-align:left;vertical-align:top;padding:10px;border-bottom:1px solid #d9e4e1;line-height:1.5}th{background:#0f6d69;color:#fff;position:sticky;top:0}.pass{color:#167242;font-weight:700}.fail{color:#b42318;font-weight:700}.skip{color:#8a5a00;font-weight:700}.detail{font-weight:400;color:#35545b}code{font-size:12px}</style>"
            + "<main><h1>推荐与路线算法独立评测</h1><p>本地规则通过 " + local.Count(result => result.Passed).ToString(CultureInfo.InvariantCulture) + "/10。AI + 规则：" + Html(aiNote) + "</p>"
            + "<p class=\"note\">固定输入：<code>evaluation/data/cases.json</code>。地点快照、案例原始输出、CSV 与问题报告均保存在本次输出目录。默认路线为本地演示估算，非道路导航；未知价格、营业时间和排队会保留为未核实信息。</p>"
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
        return string.Join("；", parts);
    }

    private static string RenderIssues(IReadOnlyList<CaseResult> results)
    {
        var issueCases = results.Where(result => result.Strategy == "local_rule" && result.Status == "error").Take(3).ToArray();
        var report = new StringBuilder("# 最值得讨论的 3 个评测发现\n\n");
        if (issueCases.Length == 0) return report.Append("本次本地规则未出现失败案例；请审阅 HTML 报告中的未核实信息。\n").ToString();
        foreach (var result in issueCases)
        {
            report.Append("## ").Append(result.Id).Append(" - ").Append(result.Title).Append("\n\n")
                .Append("- 对应模块：`").Append(result.TestCase.Str("module")).Append("`\n")
                .Append("- 输入：`").Append(result.TestCase.Object("profile")!.ToJsonString()).Append("`\n")
                .Append("- 实际输出：").Append(result.Error).Append("\n")
                .Append("- 期望行为：").Append(result.TestCase.Object("expected")!.Str("expectedBehavior")).Append("\n")
                .Append("- 评测结论：这是明确失败而非静默满足；是否要改善提示或补足固定证据，应由产品和后端共同决定。\n\n");
        }
        return report.ToString();
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string Html(string value) => WebUtility.HtmlEncode(value);

    private sealed record Assessment(bool Passed, string Summary);

    private sealed record CaseResult(JsonObject TestCase, string Strategy, string Status, bool Passed, string Summary, JsonObject? Output, string? Error)
    {
        public string Id => TestCase.Str("id");
        public string Title => TestCase.Str("title");
        public static CaseResult NotRun(JsonObject testCase, string strategy, string reason) => new(testCase, strategy, "not_run", true, reason, null, reason);
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

    private sealed record Options(string? Root, string? Output, bool WithAi)
    {
        public static Options Parse(IReadOnlyList<string> args)
        {
            string? root = null;
            string? output = null;
            var withAi = false;
            for (var index = 0; index < args.Count; index++)
            {
                switch (args[index])
                {
                    case "--root" when index + 1 < args.Count: root = Path.GetFullPath(args[++index]); break;
                    case "--output" when index + 1 < args.Count: output = Path.GetFullPath(args[++index]); break;
                    case "--with-ai": withAi = true; break;
                    default: throw new ArgumentException("Usage: dotnet run --project server-dotnet/SydneyBuddy.Evaluation -- [--root path] [--output path] [--with-ai]");
                }
            }
            return new Options(root, output, withAi);
        }
    }
}
