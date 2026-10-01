using Microsoft.Extensions.Caching.Memory;
using OpsDesk.Application.Abstractions;

namespace OpsDesk.Infrastructure.Time;

public class HolidayCache(IMemoryCache cache) : IHolidayCache
{
    public void Invalidate() => cache.Remove(CachedHolidayProvider.CacheKey);
}
