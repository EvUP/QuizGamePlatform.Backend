namespace QuizGamePlatform.Backend.Core.Abstractions
{
    public interface IMatchLockProvider
    {
        // лок конкретного матча чтобы его переходы не шли параллельно
        SemaphoreSlim GetLock(Guid matchId);
    }
}
