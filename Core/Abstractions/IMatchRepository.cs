using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Core.Abstractions
{
    public interface IMatchRepository
    {
        /// <summary>
        /// Добавить новый матч (без сохранения)
        /// </summary>
        Task AddMatchAsync(MatchEntity match, CancellationToken ct);
        /// <summary>
        /// Найти матч с его вопросами
        /// </summary>
        Task<MatchEntity?> GetMatchAsync(Guid matchId, CancellationToken ct);
        /// <summary>
        /// Найти вопрос матча по порядковому номеру (с вариантами ответа)
        /// </summary>
        Task<MatchQuestionEntity?> GetMatchQuestionAsync(Guid matchId, int order, CancellationToken ct);
        /// <summary>
        /// Id матчей с истёкшим вопросом
        /// </summary>
        Task<List<Guid>> GetExpiredActiveMatchIdsAsync(DateTime nowUtc, CancellationToken ct);
        /// <summary>
        /// Добавить ответ игрока (без сохранения)
        /// </summary>
        Task AddAnswerAsync(PlayerAnswerEntity answer, CancellationToken ct);
        /// <summary>
        /// Сохранить изменения
        /// </summary>
        Task SaveChangesAsync(CancellationToken ct);
    }
}
