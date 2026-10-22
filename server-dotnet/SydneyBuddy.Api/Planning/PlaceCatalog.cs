using Microsoft.Extensions.Caching.Memory;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>
/// 只缓存本地公共景点目录 5 分钟；每次交付深拷贝，避免请求修改污染缓存。
/// Caches only the public local place catalog for five minutes and returns deep clones to prevent mutation leaks.
/// </summary>
public sealed class PlaceCatalog(CatalogCache catalogCache, ProjectPaths paths)
{
    public IReadOnlyList<JsonObject> Get()
    {
        var key = $"SydneyBuddy.PlaceCatalog:{paths.ServerDirectory}";
        var stored = catalogCache.Cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            entry.Size = 1;
            var raw = JsonNode.Parse(File.ReadAllText(Path.Combine(paths.ServerDirectory, "places.json"))) as JsonArray
                ?? throw new InvalidOperationException("The place catalog must be a JSON array.");
            return raw.Select(node => node as JsonObject
                ?? throw new InvalidOperationException("The place catalog contains an invalid record.")).ToArray();
        }) ?? throw new InvalidOperationException("The place catalog could not be loaded.");
        return stored.Select(place => (JsonObject)place.DeepClone()).ToArray();
    }
}
