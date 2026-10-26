using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>Trip-revision-scoped checks over saved route observations. Never invents an optimized itinerary.</summary>
public sealed class TravelWorldModelService(WorldRepository worlds)
{
    public async Task<JsonObject> SimulateAsync(string tripId, string requestId, CancellationToken ct = default)
    {
        ValidateId(tripId); ValidateId(requestId);
        await using var transaction = await worlds.BeginAsync("world-trip:" + tripId, ct);
        var (trip, revision) = await worlds.GetTripAsync(tripId, ct);
        var world = await worlds.EnsureAsync(tripId, ct);
        var existing = await worlds.FindRunAsync(world.Id, requestId, ct);
        if (existing is not null) { var result = await worlds.GetRunAsync(existing.Id, ct); await transaction.CommitAsync(ct); return result; }
        var previous = await worlds.GetLatestForTripAsync(tripId, ct);
        var evaluation = Evaluate(trip); // Validate before recording any run.
        var run = await worlds.CreateRunAsync(world, requestId, revision, ct);
        await worlds.AppendEventAsync(run, "simulation_started", "simulation:" + requestId, "simulation_engine",
            new JsonObject { ["tripId"] = tripId, ["tripRevision"] = revision, ["source"] = "saved_route_observations" }, ct);
        foreach (var snapshot in evaluation.Array("snapshots")!.OfType<JsonObject>())
            await worlds.AppendSnapshotAsync(run, (JsonObject)snapshot.DeepClone(), ct);
        await worlds.AppendSnapshotAsync(run, new JsonObject {
            ["day"] = trip.Array("days")!.Count, ["eventType"] = "simulation_completed", ["time"] = "", ["location"] = "Trip check complete",
            ["spent"] = evaluation.Object("summary")!.Number("cost"), ["steps"] = evaluation.Object("summary")!.Int("walkingSteps"),
            ["transportMinutes"] = evaluation.Object("summary")!.Int("transportMinutes"), ["energyRisk"] = evaluation.Object("summary")!.Str("energyRisk"),
            ["weatherRisk"] = evaluation.Object("summary")!.Str("weatherRisk"), ["nextStop"] = "View check results",
            ["summary"] = evaluation["summary"]!.DeepClone(), ["suggestions"] = evaluation["suggestions"]!.DeepClone()
        }, ct);
        await worlds.AppendEventAsync(run, "simulation_completed", "simulation_complete:" + requestId, "simulation_engine", new JsonObject { ["source"] = "saved_route_rules" }, ct);
        // Keep same-revision refusals across rechecks; changed itineraries must be evaluated afresh.
        if (previous.Object("run") is {} prior && prior.Int("tripRevision") == revision) {
            foreach (var choice in prior.Array("decisions")!.OfType<JsonObject>().Where(x=>x.Str("decision")=="rejected")) {
                var id = choice.Str("suggestionId");
                if (evaluation.Array("suggestions")!.OfType<JsonObject>().Any(x=>x.Str("id")==id))
                    await worlds.AppendEventAsync(run,"suggestion_decision","decision:"+run.Id+":"+id,"preserved_user_choice",
                        new JsonObject { ["suggestionId"]=id,["decision"]="rejected",["preserveUserChoice"]=true,["fromRunId"]=prior.Str("id") },ct);
            }
        }
        await worlds.CompleteRunAsync(run, ct);
        var rendered = await worlds.GetRunAsync(run.Id, ct);
        await transaction.CommitAsync(ct);
        return rendered;
    }

    public async Task<JsonObject> DecideAsync(string runId, string suggestionId, string decision, CancellationToken ct = default)
    {
        ValidateId(runId); ValidateId(suggestionId);
        if (decision is not ("accepted" or "rejected")) throw new ServiceException("The decision must be accepted or rejected.");
        await using var transaction = await worlds.BeginAsync("world-run:" + runId, ct);
        var rendered = await worlds.GetRunAsync(runId, ct);
        if (rendered.Bool("stale")) throw new ServiceException("Your trip has changed. Run the check again before making adjustments.");
        if (!(rendered.Array("suggestions")?.OfType<JsonObject>().Any(x => x.Str("id") == suggestionId) ?? false)) throw new ServiceException("This suggestion does not belong to this check.");
        var previous = rendered.Array("decisions")?.OfType<JsonObject>().FirstOrDefault(x => x.Str("suggestionId") == suggestionId);
        if (previous is not null) {
            if (previous.Str("decision") != decision) throw new ServiceException("This suggestion already has a decision. Run the check again or edit the day's schedule directly.");
            await transaction.CommitAsync(ct); return rendered;
        }
        var run = new WorldRunEntity { Id = runId, WorldId = rendered.Str("worldId"), RequestId = "", Kind = "", TripRevision = 0, Status = "", CreatedAt = "" };
        await worlds.AppendEventAsync(run, "suggestion_decision", "decision:" + runId + ":" + suggestionId, "user",
            new JsonObject { ["suggestionId"] = suggestionId, ["decision"] = decision, ["preserveUserChoice"] = decision == "rejected" }, ct);
        var result = await worlds.GetRunAsync(runId, ct); await transaction.CommitAsync(ct); return result;
    }

    public static JsonObject Evaluate(JsonObject trip)
    {
        var profile = trip.Object("profile") ?? throw new ServiceException("Trip settings are missing, so the trip cannot be checked.");
        var days = trip.Array("days")?.OfType<JsonObject>().ToArray() ?? [];
        if (days.Length is <1 or >15) throw new ServiceException("The trip has no dates to check.");
        var snapshots = new JsonArray(); var findings = new JsonArray(); var daily = new JsonArray();
        var settings = trip.Object("schedule")?.Object("settings");
        var weather = trip.Object("weather");
        for (var d = 0; d < days.Length; d++) {
            var day = days[d];
            var scheduleDay = trip.Object("schedule")?.Array("days")?.OfType<JsonObject>().FirstOrDefault(x=>x.Str("date")==day.Str("date"));
            var limit = Math.Max(1, scheduleDay?.Int("maxSteps",settings?.Int("maxSteps",profile.Int("maxSteps",10000)) ?? profile.Int("maxSteps",10000)) ?? settings?.Int("maxSteps",profile.Int("maxSteps",10000)) ?? profile.Int("maxSteps",10000));
            var budget = Math.Max(0, scheduleDay?.Number("dailyBudget",settings?.Number("dailyBudget",profile.Number("budget")) ?? profile.Number("budget")) ?? settings?.Number("dailyBudget",profile.Number("budget")) ?? profile.Number("budget"));
            var stops = day.Array("stops")?.OfType<JsonObject>().ToArray() ?? [];
            var legs = day.Array("legs")?.OfType<JsonObject>().ToArray() ?? [];
            var steps = Math.Max(0, day.Int("estimatedSteps")); var cost = Math.Max(0, day.Number("estimatedCost"));
            var transport = (int)Math.Ceiling(legs.Sum(x => Math.Max(0, x.Number("minutes"))));
            var routeKnown = legs.Length == stops.Length + 1 && legs.All(x => !string.IsNullOrWhiteSpace(x.Str("source")) && !x.Str("source").Contains("estimate", StringComparison.OrdinalIgnoreCase) && !x.Str("source").Contains("demo", StringComparison.OrdinalIgnoreCase) && !new[]{"估算","估計","示例","演示"}.Any(word=>x.Str("source").Contains(word)) && !x.Bool("unreachable"));
            var forecast = weather?.Array("days")?.OfType<JsonObject>().FirstOrDefault(x => x.Str("date") == day.Str("date"));
            var weatherKnown = forecast?.Bool("available") == true;
            var rainy = weatherKnown && forecast!.Bool("rainy");
            var outdoor = stops.Where(x => !x.Bool("indoor") && x["indoorKnown"]?.ToString() != "false").Sum(x => Math.Max(0, x.Int("visitMinutes")));
            var costKnown = stops.All(x => x["costKnown"]?.ToString() != "false");
            var indoorKnown = stops.All(x => x["indoorKnown"]?.ToString() != "false");
            var energyRisk = Risk(steps, limit); var budgetRisk = budget <= 0 ? "unknown" : costKnown ? Risk(cost, budget) : cost > budget ? "high" : "unknown";
            var weatherRisk = !weatherKnown || !indoorKnown ? "unknown" : rainy && outdoor > 0 ? "high" : "low";
            var row = new JsonObject { ["day"] = d + 1, ["date"] = day.Str("date"), ["walkingSteps"] = steps, ["cost"] = cost,
                ["transportMinutes"] = transport, ["outdoorMinutes"] = outdoor, ["energyRisk"] = energyRisk, ["budgetRisk"] = budgetRisk,
                ["weatherRisk"] = weatherRisk, ["routeKnown"] = routeKnown, ["costKnown"] = costKnown, ["weatherKnown"] = weatherKnown };
            daily.Add(row);
            var start = scheduleDay?.Int("startMinute",day.Int("planningStartMinute",profile.Int("startMinute",600))) ?? day.Int("planningStartMinute",profile.Int("startMinute",600));
            snapshots.Add(State(d+1,"day_started",Time(start),day.Object("start")?.Str("name") ?? "Starting point",0,0,0,"low","unknown",stops.FirstOrDefault()?.Str("name") ?? "Return point"));
            var spent = 0d; var walked = 0; var elapsed = 0;
            for (var i = 0; i < stops.Length; i++) {
                var stop = stops[i]; spent += Math.Max(0, stop.Number("cost"));
                // Saved per-stop steps reflect its inbound leg and activity. Do not spread daily steps evenly.
                walked += Math.Max(0, stop.Int("steps")); elapsed += (int)Math.Ceiling(Math.Max(0, legs.ElementAtOrDefault(i)?.Number("minutes") ?? 0));
                snapshots.Add(State(d+1,"arrived",stop.Str("arrival",Time(start)),stop.Str("name"),spent,walked,elapsed,Risk(walked,limit),
                    !weatherKnown || stop["indoorKnown"]?.ToString()=="false" ? "unknown" : rainy && !stop.Bool("indoor") ? "high":"low",
                    stops.ElementAtOrDefault(i+1)?.Str("name") ?? day.Object("end")?.Str("name") ?? "Return point"));
            }
            snapshots.Add(State(d+1,"returned",day.Str("finishTime"),day.Object("end")?.Str("name") ?? "Return point",cost,steps,transport,energyRisk,"unknown","Day check complete"));
            void Add(string type, string title, string detail, string action) => findings.Add(new JsonObject { ["id"] = type+"-"+d, ["dayIndex"] = d, ["title"] = title, ["detail"] = detail, ["action"] = action, ["status"] = "pending" });
            if (energyRisk is "high" or "medium") Add("energy","This day could be tiring",$"Day {d+1} has about {steps:N0} steps, against your daily limit of {limit:N0}. Remove optional stops or leave time to rest.","adjust");
            if (budgetRisk == "high") Add("budget","This day is stretching your budget",$"Day {d+1} activities are estimated at A${cost:0.##} per person, against a daily budget of A${budget:0.##}. Unconfirmed costs are excluded.","adjust");
            if (weatherRisk == "high") Add("weather","Shall we find somewhere out of the rain?",$"The saved forecast for day {d+1} shows rain, with about {outdoor} minutes outdoors. Weather can change; please check it first.","replace");
            if (transport >= 120) Add("transport","This day has more travel time",$"Day {d+1} has about {transport} minutes of transport. Compare the routes to assess detours; total duration alone is not enough.","adjust");
            if (!routeKnown || !weatherKnown || !costKnown || !indoorKnown) Add("verify","Some details still need checking",$"Day {d+1}: "+string.Join(", ",new[]{!routeKnown?"some transport is estimated":null,!weatherKnown?"the day's weather is unknown":null,!costKnown?"some costs are unknown":null,!indoorKnown?"some indoor/outdoor details are unknown":null}.Where(x=>x is not null))+". Unknown does not mean risk-free.","review");
        }
        string Overall(string key) {var values=daily.OfType<JsonObject>().Select(x=>x.Str(key)).ToArray();return values.Contains("high")?"high":values.Contains("unknown")?"unknown":values.Contains("medium")?"medium":"low";}
        return new JsonObject { ["summary"] = new JsonObject {
            ["walkingSteps"] = daily.OfType<JsonObject>().Sum(x=>x.Int("walkingSteps")), ["cost"] = daily.OfType<JsonObject>().Sum(x=>x.Number("cost")),
            ["transportMinutes"] = daily.OfType<JsonObject>().Sum(x=>x.Int("transportMinutes")), ["outdoorMinutes"] = daily.OfType<JsonObject>().Sum(x=>x.Int("outdoorMinutes")),
            ["energyRisk"] = Overall("energyRisk"), ["budgetRisk"] = Overall("budgetRisk"), ["weatherRisk"] = Overall("weatherRisk"),
            ["days"] = daily, ["source"] = "Saved routes and forecasts; steps and costs are estimates, not live location or actual spending", ["weatherFetchedAt"] = weather?.Str("fetchedAt") ?? ""
        }, ["suggestions"] = findings, ["snapshots"] = snapshots };
    }
    private static JsonObject State(int day,string type,string time,string location,double cost,int steps,int transport,string energy,string weather,string next) => new() {
        ["day"]=day,["eventType"]=type,["time"]=time,["location"]=location,["spent"]=Math.Round(cost,2),["steps"]=steps,["transportMinutes"]=transport,["energyRisk"]=energy,["weatherRisk"]=weather,["nextStop"]=next };
    private static string Risk(double value,double limit) => (value/Math.Max(1,limit)) switch { >1=>"high", >.8=>"medium", _=>"low" };
    private static string Time(int minute) => $"{Math.Clamp(minute,0,1439)/60:00}:{Math.Clamp(minute,0,1439)%60:00}";
    private static void ValidateId(string value) { if(string.IsNullOrWhiteSpace(value)||value.Length>120||value.Any(x=>!(char.IsLetterOrDigit(x)||x is '-' or '_')))throw new ServiceException("Invalid check ID."); }
}
