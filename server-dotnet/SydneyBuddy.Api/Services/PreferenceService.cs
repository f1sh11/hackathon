using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 协调授权记忆与数据库摘要，数据库写入失败时撤回刚添加的记忆。
/// Coordinates consented H-MEM records and database summaries with compensation on failed saves.
/// </summary>
public sealed class PreferenceService(IMemoryService memory, MemoryRepository records, ILogger<PreferenceService> logger)
{
    public async Task<JsonObject> SaveAsync(JsonObject input, CancellationToken ct = default)
    {
        if (!input.Bool("consent")) throw new ServiceException("需要你明确同意保存记忆");
        var profile = input.Object("profile") ?? new JsonObject();
        var summary = input.Str("summary");
        if (summary.Length > 500) summary = summary[..500];
        var record = await memory.RunAsync("add", new JsonObject { ["content"] = "travel preference Sydney: " + summary }, ct);
        var saved = new JsonObject
        {
            ["id"] = record.Str("id"), ["summary"] = summary,
            ["profile"] = new JsonObject { ["interests"] = profile.Array("interests")?.DeepClone() ?? new JsonArray(), ["maxSteps"] = profile.Int("maxSteps", 10000) },
            ["provenance"] = new JsonObject { ["source"] = "user_confirmed", ["createdAt"] = DateTimeOffset.UtcNow.ToString("O") },
            ["scope"] = "local_only", ["consent"] = true
        };
        try { await records.SaveAsync(saved, ct); }
        catch
        {
            // 两个存储没有分布式事务，使用可重试的撤回补偿。 / Use retryable compensation across the two stores.
            try { await memory.RunAsync("delete", new JsonObject { ["id"] = record.Str("id") }, CancellationToken.None); }
            catch (ServiceException)
            {
                logger.LogError("Preference compensation failed; manual reconciliation may be required. Request data omitted.");
            }
            throw;
        }
        return saved;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await memory.RunAsync("delete", new JsonObject { ["id"] = id }, ct);
        await records.DeleteAsync(id, ct);
    }
}
