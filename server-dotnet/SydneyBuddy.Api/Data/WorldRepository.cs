using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// PostgreSQL-backed append store for the travel World Model. Consumers receive projections rather than querying tables directly.
/// </summary>
public sealed class WorldRepository(BuddyDbContext database)
{
    // One transaction per simulation: interruption rolls back all snapshots. The lock also serializes retries across API instances.
    public async Task<IDbContextTransaction> BeginAsync(string key, CancellationToken ct) {
        var transaction = await database.Database.BeginTransactionAsync(ct);
        try { await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct); return transaction; }
        catch { await transaction.DisposeAsync(); throw; }
    }
    public async Task<(JsonObject Trip, int Revision)> GetTripAsync(string tripId, CancellationToken ct = default)
    {
        var row = await database.Trips.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tripId, ct)
            ?? throw new ServiceException("请先保存行程，再启动数字分身模拟。");
        return (ParseObject(row.DataJson), row.Revision);
    }

    public async Task<TravelWorldEntity> EnsureAsync(string tripId, CancellationToken ct = default)
    {
        var existing = await database.Worlds.SingleOrDefaultAsync(x => x.TripId == tripId, ct);
        if (existing is not null) return existing;

        var world = new TravelWorldEntity
        {
            Id = "world_" + Hash("trip:" + tripId), TripId = tripId, SchemaVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        database.Worlds.Add(world);
        try { await database.SaveChangesAsync(ct); return world; }
        catch (DbUpdateException)
        {
            database.Entry(world).State = EntityState.Detached;
            return await database.Worlds.SingleAsync(x => x.TripId == tripId, ct);
        }
    }

    public async Task<WorldRunEntity?> FindRunAsync(string worldId, string requestId, CancellationToken ct = default) =>
        await database.WorldRuns.AsNoTracking().SingleOrDefaultAsync(x => x.WorldId == worldId && x.RequestId == requestId, ct);

    public async Task<WorldRunEntity> CreateRunAsync(TravelWorldEntity world, string requestId, int revision, CancellationToken ct = default,string kind="travel_simulation")
    {
        var run = new WorldRunEntity
        {
            Id = "run_" + Guid.NewGuid().ToString("N"), WorldId = world.Id, RequestId = requestId,
            Kind = kind, TripRevision = revision, Status = "running",
            CreatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        database.WorldRuns.Add(run);
        await database.SaveChangesAsync(ct);
        return run;
    }

    public async Task AppendSnapshotAsync(WorldRunEntity run, JsonObject state, CancellationToken ct = default)
    {
        var sequence = await database.WorldSnapshots.CountAsync(x => x.RunId == run.Id, ct);
        database.WorldSnapshots.Add(new WorldSnapshotEntity
        {
            Id = "snapshot_" + Guid.NewGuid().ToString("N"), WorldId = run.WorldId, RunId = run.Id,
            Sequence = sequence, OccurredAt = DateTimeOffset.UtcNow.ToString("O"), DataJson = state.ToJsonString()
        });
        await database.SaveChangesAsync(ct);
    }

    public async Task AppendEventAsync(WorldRunEntity run, string type, string idempotencyKey, string actor, JsonObject data, CancellationToken ct = default)
    {
        if (await database.WorldEvents.AnyAsync(x => x.WorldId == run.WorldId && x.IdempotencyKey == idempotencyKey, ct)) return;
        database.WorldEvents.Add(new WorldEventEntity
        {
            Id = "event_" + Hash(run.WorldId + ":" + idempotencyKey), WorldId = run.WorldId, RunId = run.Id,
            Type = type, IdempotencyKey = idempotencyKey, Actor = actor,
            OccurredAt = DateTimeOffset.UtcNow.ToString("O"), DataJson = data.ToJsonString()
        });
        await database.SaveChangesAsync(ct);
    }

    public async Task CompleteRunAsync(WorldRunEntity run, CancellationToken ct = default)
    {
        var persisted = await database.WorldRuns.SingleAsync(x => x.Id == run.Id, ct);
        persisted.Status = "completed";
        persisted.CompletedAt = DateTimeOffset.UtcNow.ToString("O");
        await database.SaveChangesAsync(ct);
    }

    public async Task<JsonObject> GetRunAsync(string runId, CancellationToken ct = default)
    {
        var run = await database.WorldRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct)
            ?? throw new ServiceException("找不到这次世界状态模拟。");
        var world = await database.Worlds.AsNoTracking().SingleAsync(x => x.Id == run.WorldId, ct);
        var snapshots = await database.WorldSnapshots.AsNoTracking().Where(x => x.RunId == run.Id)
            .OrderBy(x => x.Sequence).ToListAsync(ct);
        var events = await database.WorldEvents.AsNoTracking().Where(x => x.RunId == run.Id)
            .OrderBy(x => x.OccurredAt).ToListAsync(ct);

        var renderedSnapshots = new JsonArray(snapshots.Select(RenderSnapshot).ToArray());
        var finalState = renderedSnapshots.LastOrDefault() as JsonObject;
        var decisions = new JsonArray(events.Where(x => x.Type == "suggestion_decision").Select(RenderEvent).ToArray());
        return new JsonObject
        {
            ["id"] = run.Id, ["worldId"] = world.Id, ["tripId"] = world.TripId,
            ["kind"] = run.Kind, ["tripRevision"] = run.TripRevision, ["status"] = run.Status,
            ["createdAt"] = run.CreatedAt, ["completedAt"] = run.CompletedAt,
            ["stale"] = (await GetTripAsync(world.TripId, ct)).Revision != run.TripRevision,
            ["summary"] = finalState?["summary"]?.DeepClone() ?? new JsonObject(),
            ["suggestions"] = finalState?["suggestions"]?.DeepClone() ?? new JsonArray(),
            ["snapshots"] = renderedSnapshots, ["decisions"] = decisions
        };
    }

    public async Task<JsonObject> GetLatestForTripAsync(string tripId, CancellationToken ct = default,string kind="travel_simulation")
    {
        var world = await database.Worlds.AsNoTracking().SingleOrDefaultAsync(x => x.TripId == tripId, ct);
        if (world is null) return new JsonObject { ["available"] = false };
        var run = await database.WorldRuns.AsNoTracking().Where(x => x.WorldId == world.Id && x.Kind==kind)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (run is null) return new JsonObject { ["available"] = false, ["worldId"] = world.Id };
        return new JsonObject { ["available"] = true, ["run"] = await GetRunAsync(run.Id, ct) };
    }

    /// Called inside the same transaction as a user-confirmed trip save. No prompts, tokens or memories are persisted here.
    public async Task RecordPlanningAsync(JsonObject trip,CancellationToken ct)
    {
        var world=await EnsureAsync(trip.Str("id"),ct);
        var requestId="planning:revision:"+trip.Int("revision");
        if(await FindRunAsync(world.Id,requestId,ct) is not null)return;
        var run=await CreateRunAsync(world,requestId,trip.Int("revision"),ct,"itinerary_planning");
        var state=trip.Object("planningState")!;
        foreach(var item in state.Array("events")!.OfType<JsonObject>())
            await AppendEventAsync(run,item.Str("type"),requestId+":"+item.Str("type"),item.Str("source"),item,ct);
        await AppendSnapshotAsync(run,state,ct);
        await CompleteRunAsync(run,ct);
    }

    private static JsonObject RenderSnapshot(WorldSnapshotEntity row)
    {
        var state = ParseObject(row.DataJson);
        state["id"] = row.Id;
        state["sequence"] = row.Sequence;
        state["occurredAt"] = row.OccurredAt;
        return state;
    }

    private static JsonObject RenderEvent(WorldEventEntity row)
    {
        var data = ParseObject(row.DataJson);
        data["id"] = row.Id;
        data["type"] = row.Type;
        data["actor"] = row.Actor;
        data["occurredAt"] = row.OccurredAt;
        return data;
    }

    private static JsonObject ParseObject(string json) => JsonNode.Parse(json) as JsonObject
        ?? throw new InvalidOperationException("World Model storage contains an invalid JSON object.");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
}
