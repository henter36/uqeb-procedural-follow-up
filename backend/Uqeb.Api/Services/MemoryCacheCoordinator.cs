using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Uqeb.Api.Services;

public interface IMemoryCacheCoordinator
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan duration);
}

public sealed class MemoryCacheCoordinator : IMemoryCacheCoordinator
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public MemoryCacheCoordinator(IMemoryCache cache) => _cache = cache;

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan duration)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;

        var gate = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_cache.TryGetValue(key, out cached) && cached is not null)
                return cached;

            var result = await factory();
            _cache.Set(key, result, duration);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
}
