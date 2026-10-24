using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>读写行程并执行原子版本比较；persists itinerary snapshots and arbitrates concurrent saves in PostgreSQL.</summary>
public sealed class TripRepository(BuddyDbContext database)
{
    public async Task<JsonArray> ListAsync(CancellationToken ct = default)
    {
        var rows = await database.Trips.AsNoTracking().OrderBy(trip => trip.Id)
            .Select(trip => trip.DataJson).ToListAsync(ct);
        return new JsonArray(rows.Select(json => JsonNode.Parse(json)).ToArray());
    }

    public async Task<JsonObject?> FindAsync(string id,CancellationToken ct=default) {
        var json=await database.Trips.AsNoTracking().Where(t=>t.Id==id).Select(t=>t.DataJson).FirstOrDefaultAsync(ct);
        return json is null ? null:JsonNode.Parse(json)!.AsObject();
    }

    public async Task<JsonObject> SaveAsync(JsonObject draft, int expectedRevision, CancellationToken ct = default)
    {
        if (expectedRevision < 0 || expectedRevision == int.MaxValue) throw new ServiceException("行程版本不合法");
        var trip = (JsonObject)draft.DeepClone();
        var id = trip.Str("id");
        if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString();
        if (id.Length > 200) throw new ServiceException("行程标识过长");
        trip["id"] = id;
        trip["revision"] = expectedRevision + 1;
        var json = trip.ToJsonString();

        if (expectedRevision == 0)
        {
            var entity = new TripEntity { Id = id, DataJson = json, Revision = 1 };
            database.Trips.Add(entity);
            try
            {
                await database.SaveChangesAsync(ct);
                // 后续写入始终以数据库版本为准；avoid stale tracked snapshots on repeated calls in one scope.
                database.Entry(entity).State = EntityState.Detached;
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                database.Entry(entity).State = EntityState.Detached;
                throw new RevisionConflictException();
            }
        }
        else
        {
            // 一条条件 UPDATE 完成比较与写入；a single SQL statement performs the compare-and-swap.
            var updated = await database.Trips.Where(entity => entity.Id == id && entity.Revision == expectedRevision)
                .ExecuteUpdateAsync(update => update.SetProperty(entity => entity.DataJson, json)
                    .SetProperty(entity => entity.Revision, entity => entity.Revision + 1), ct);
            if (updated != 1) throw new RevisionConflictException();
        }
        return trip;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default) =>
        await database.Trips.Where(trip => trip.Id == id).ExecuteDeleteAsync(ct);
}
