using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>持久化授权偏好的 API 展示记录；owns preference metadata while the H-MEM service owns retrieval and audit history.</summary>
public sealed class MemoryRepository(BuddyDbContext database)
{
    public async Task<JsonArray> ListAsync(CancellationToken ct = default)
    {
        var rows = await database.Memories.AsNoTracking().OrderBy(memory => memory.Id)
            .Select(memory => memory.DataJson).ToListAsync(ct);
        return new JsonArray(rows.Select(json => JsonNode.Parse(json)).ToArray());
    }

    public async Task SaveAsync(JsonObject memory, CancellationToken ct = default)
    {
        database.Memories.Add(new MemoryEntity { Id = memory.Str("id"), DataJson = memory.ToJsonString() });
        await database.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default) =>
        await database.Memories.Where(memory => memory.Id == id).ExecuteDeleteAsync(ct);
}
