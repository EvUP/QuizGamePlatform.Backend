using QuizGamePlatform.Backend.Core.Abstractions;

namespace QuizGamePlatform.Backend.Core.Helpers
{
    // фиксированный набор локов, матч берёт свой по хэшу id
    // память не растёт с числом матчей, редкое совпадение просто ставит матчи в очередь
    public class MatchLockProvider : IMatchLockProvider
    {
        private const int LockCount = 1024;

        private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, LockCount)
            .Select(_ => new SemaphoreSlim(1, 1))
            .ToArray();

        public SemaphoreSlim GetLock(Guid matchId)
            => _locks[(matchId.GetHashCode() & int.MaxValue) % LockCount];
    }
}
