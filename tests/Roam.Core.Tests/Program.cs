using Roam.Core;

var planner = new Planner();
var passed = 0;
var failures = new List<string>();
void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures.Add($"{name}: {ex.Message}"); Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
void Throws(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    throw new InvalidOperationException("Expected an ArgumentException.");
}
int Minute(string value) { var t = TimeOnly.Parse(value); return t.Hour * 60 + t.Minute; }
PlanningRequest Request() => new(new DateOnly(2026, 10, 24), new TimeOnly(9, 0), new TimeOnly(18, 0), 120m, 100, "harbour", "");
var middle = ProfileFactory.Create([1, 1, 1, 1, 1, 1]);
void CheckPlan(TravelerProfile profile, PlanningRequest request, PlanningResult result)
{
    if (!result.Feasible)
    {
        Assert(result.Plan is null, "Infeasible result cannot contain a plan.");
        Assert(result.Conflicts.Length > 0, "Infeasible result needs a useful explanation.");
        return;
    }
    var plan = result.Plan ?? throw new InvalidOperationException("Feasible result has no plan.");
    Assert(result.Conflicts.Length == 0, "Feasible result cannot have blocking conflicts.");
    Assert(plan.Stops.Length >= 2, "Plan needs an attraction and a return leg.");
    Assert(plan.Stops[^1].Id == "return", "Return leg must be explicitly included.");
    Assert(plan.Stops.Select(stop => stop.Id).Distinct().Count() == plan.Stops.Length, "No duplicate stops.");
    Assert(plan.Stops.All(stop => stop.Id != "unverified"), "Unknown opening hours cannot be assumed open.");
    Assert(plan.TotalCost == plan.Stops.Sum(stop => stop.Cost), "Every charge must be accounted for, including return.");
    Assert(plan.WalkKm == plan.Stops.Sum(stop => stop.WalkKm), "Every walking segment must be accounted for.");
    Assert(plan.TotalCost <= request.Budget, "Budget exceeded.");
    Assert(plan.TotalCost >= 0, "Cost cannot be negative.");
    Assert(Minute(plan.ReturnTime) <= Minute(request.EndTime.ToString("HH:mm")), "Return deadline exceeded.");
    Assert(plan.ReturnTime == plan.Stops[^1].EndTime, "Return summary must agree with final stop.");
    Assert(plan.RemainingHp == plan.Stops[^1].RemainingHp, "HP summary must agree with final stop.");
    Assert(plan.RemainingHp >= Math.Ceiling(request.Energy * (request.Easier ? .4 : .25)), "No final HP reserve.");
    Assert(plan.Stops.All(stop => stop.RemainingHp > 0 && stop.RemainingHp <= request.Energy), "Prefix HP cannot be exhausted or over-restored.");
    var previousEnd = Minute(request.StartTime.ToString("HH:mm"));
    foreach (var stop in plan.Stops)
    {
        Assert(Minute(stop.StartTime) >= previousEnd, "Stops overlap or precede departure.");
        Assert(stop.Cost >= 0 && stop.WalkKm >= 0, "Negative stop resources.");
        if (stop.Id == "return")
        {
            Assert(Minute(stop.EndTime) - Minute(stop.StartTime) == stop.TransferMinutes, "Return duration missing.");
        }
        else
        {
            Assert(Minute(stop.StartTime) >= previousEnd + stop.TransferMinutes, "Incoming transfer missing.");
            var place = planner.Places.Single(place => place.Id == stop.Id);
            Assert(Minute(stop.StartTime) >= place.OpensAt && Minute(stop.EndTime) <= place.ClosesAt, "Opening hours violated.");
            Assert(Minute(stop.EndTime) - Minute(stop.StartTime) == stop.DurationMinutes, "Activity duration mismatch.");
            if (place.IsMeal) Assert(Minute(stop.StartTime) is >= 690 and <= 855, "Lunch scheduled at an unreasonable hour.");
        }
        previousEnd = Minute(stop.EndTime);
    }
    var duration = Minute(plan.ReturnTime) - Minute(request.StartTime.ToString("HH:mm"));
    if (duration >= 240 && Minute(request.StartTime.ToString("HH:mm")) <= 810 && Minute(plan.ReturnTime) >= 750)
        Assert(plan.Stops.Any(stop => stop.Id is "lunch" or "picnic"), "Long midday route needs lunch.");
    Assert(plan.DataNotice.Contains("示例"), "Fixture data must be clearly labelled.");
}

Test("profile derives values from six answers", () =>
{
    var answers = new[] { 0, 1, 2, 0, 1, 2 };
    var profile = ProfileFactory.Create(answers);
    Assert(profile.Values.SequenceEqual(new[] { 25, 55, 85, 25, 55, 85 }), "Incorrect value mapping.");
    answers[0] = 2;
    Assert(profile.Answers[0] == 0, "Caller mutation changed stored answers.");
    Assert(profile.Id != Guid.Empty, "Profile needs an ID.");
});
Test("incomplete or manipulated profile is rejected", () =>
{
    Throws(() => ProfileFactory.Create([0, 1]));
    Throws(() => ProfileFactory.Create([0, 0, 0, 0, 0, 3]));
    Throws(() => planner.Generate(middle with { Values = [100, 100, 100, 100, 100, 100] }, Request()));
});
Test("ordinary itinerary is complete and consistent", () =>
{
    var result = planner.Generate(middle, Request());
    Assert(result.Feasible, "Ordinary day should have feasible options.");
    CheckPlan(middle, Request(), result);
});
Test("budget equality is allowed", () =>
{
    var result = planner.Generate(middle, Request());
    var request = Request() with { Budget = result.Plan!.TotalCost };
    var exact = planner.Generate(middle, request);
    Assert(exact.Feasible, "An exact budget must not be rejected.");
    CheckPlan(middle, request, exact);
});
Test("return deadline equality is allowed", () =>
{
    var result = planner.Generate(middle, Request());
    var request = Request() with { EndTime = TimeOnly.Parse(result.Plan!.ReturnTime) };
    var exact = planner.Generate(middle, request);
    Assert(exact.Feasible, "Exact return deadline should be feasible.");
    CheckPlan(middle, request, exact);
});
Test("zero budget stays valid and never silently increases", () =>
{
    var request = Request() with { Budget = 0m };
    var result = planner.Generate(middle, request);
    CheckPlan(middle, request, result);
    if (result.Feasible) Assert(result.Plan!.TotalCost == 0, "A free trip must be free.");
});
Test("invalid inputs are rejected before planning", () =>
{
    Throws(() => planner.Generate(middle, Request() with { EndTime = new TimeOnly(8, 0) }));
    Throws(() => planner.Generate(middle, Request() with { EndTime = new TimeOnly(9, 0) }));
    Throws(() => planner.Generate(middle, Request() with { Budget = -1 }));
    Throws(() => planner.Generate(middle, Request() with { Energy = 0 }));
    Throws(() => planner.Generate(middle, Request() with { Energy = 101 }));
    Throws(() => planner.Generate(middle, Request() with { Theme = "unknown" }));
});
Test("late and impossibly short requests report conflicts", () =>
{
    foreach (var request in new[] { Request() with { StartTime = new TimeOnly(22, 0), EndTime = new TimeOnly(23, 0) }, Request() with { EndTime = new TimeOnly(9, 10) } })
    {
        var result = planner.Generate(middle, request);
        Assert(!result.Feasible, "Impossible fixture request should fail.");
        CheckPlan(middle, request, result);
    }
});
Test("low energy cannot be rescued by a later cafe", () =>
{
    var request = Request() with { Energy = 10 };
    var result = planner.Generate(middle, request);
    Assert(!result.Feasible, "An exhausted prefix must not be hidden by recovery.");
    CheckPlan(middle, request, result);
});
Test("theme and profile choices actually affect route selection", () =>
{
    var culture = planner.Generate(middle, Request() with { Theme = "culture" }).Plan!;
    var nature = planner.Generate(middle, Request() with { Theme = "nature" }).Plan!;
    Assert(!culture.Stops.Select(stop => stop.Id).SequenceEqual(nature.Stops.Select(stop => stop.Id)), "Theme was ignored.");
    var slow = planner.Generate(ProfileFactory.Create([1, 1, 0, 1, 1, 1]), Request()).Plan!;
    var busy = planner.Generate(ProfileFactory.Create([1, 1, 2, 1, 1, 1]), Request()).Plan!;
    Assert(slow.Stops.Length < busy.Stops.Length, "Density preference was ignored.");
});
Test("easier version meaningfully reduces load without lowering final HP", () =>
{
    var normal = planner.Generate(middle, Request()).Plan!;
    var request = Request() with { Easier = true };
    var result = planner.Generate(middle, request);
    Assert(result.Feasible, "Ordinary day should have an easier option.");
    CheckPlan(middle, request, result);
    var easier = result.Plan!;
    Assert(easier.RemainingHp >= normal.RemainingHp, "Easier plan reduced remaining HP.");
    Assert(easier.WalkKm <= normal.WalkKm, "Easier plan increased walking.");
    Assert(Minute(easier.ReturnTime) <= Minute(normal.ReturnTime), "Easier plan ends later.");
    Assert(easier.Stops.Length < normal.Stops.Length || easier.WalkKm < normal.WalkKm || easier.RemainingHp > normal.RemainingHp, "No meaningful improvement.");
});
Test("deterministic generation preserves comparable versions", () =>
{
    var a = planner.Generate(middle, Request()).Plan!;
    var b = planner.Generate(middle, Request()).Plan!;
    Assert(a.Stops.Select(stop => stop.Id).SequenceEqual(b.Stops.Select(stop => stop.Id)), "Route changes without input changes.");
    Assert(a.TotalCost == b.TotalCost && a.RemainingHp == b.RemainingHp, "Metrics change without input changes.");
});
Test("160 randomized requests maintain constraints at every prefix", () =>
{
    var random = new Random(251022);
    var feasible = 0;
    for (var i = 0; i < 160; i++)
    {
        var profile = ProfileFactory.Create(Enumerable.Range(0, 6).Select(_ => random.Next(3)).ToArray());
        var start = random.Next(7 * 60, 18 * 60);
        var end = Math.Min(23 * 60, start + random.Next(35, 601));
        var request = Request() with { StartTime = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(start)), EndTime = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(end)), Budget = random.Next(0, 141), Energy = random.Next(10, 101), Theme = new[] { "harbour", "culture", "nature" }[random.Next(3)], Easier = i % 9 == 0 };
        var result = planner.Generate(profile, request);
        CheckPlan(profile, request, result);
        if (result.Feasible) feasible++;
    }
    Assert(feasible > 50, "Randomized fixture coverage lacks feasible cases.");
    Console.WriteLine($"  Randomized coverage: {feasible} feasible and {160 - feasible} infeasible requests.");
});
Console.WriteLine($"\n{passed} tests passed; {failures.Count} failed.");
return failures.Count == 0 ? 0 : 1;
