using Microsoft.Extensions.Caching.Memory;

namespace SydneyBuddy.Api;

/// <summary>
/// 为公共景点目录提供独立、有容量限制的缓存；不改变 EF Core 使用的共享缓存策略。
/// Provides a dedicated bounded cache for public catalogs without constraining EF Core's shared cache.
/// </summary>
public sealed class CatalogCache : IDisposable
{
    public IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions { SizeLimit = 32 });
    public void Dispose() => Cache.Dispose();
}
