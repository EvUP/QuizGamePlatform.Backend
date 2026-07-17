using System.Collections.Concurrent;
using QuizGamePlatform.Backend.Core.Abstractions;

namespace QuizGamePlatform.Backend.Core.Helpers
{
    // по локу на каждый матч, общий на всё приложение
    public class MatchLockProvider : IMatchLockProvider
    {
        private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

        public SemaphoreSlim GetLock(Guid matchId)
            => _locks.GetOrAdd(matchId, _ => new SemaphoreSlim(1, 1));
    }
}
