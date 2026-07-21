using Microsoft.Extensions.Caching.Distributed;

namespace QuizGamePlatform.Backend.DataAccess.Caching;

public static class CachePolicy
{
    public static DistributedCacheEntryOptions Questions =>
        new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(30));

    public static DistributedCacheEntryOptions Lists =>
        new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(15));

    public static DistributedCacheEntryOptions MatchTimers =>
        new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(2));

    public static DistributedCacheEntryOptions Rooms =>
       new DistributedCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(30));
}
