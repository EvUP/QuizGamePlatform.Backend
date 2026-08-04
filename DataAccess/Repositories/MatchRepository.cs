using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Repositories
{
    public class MatchRepository(ApplicationDbContext context) : IMatchRepository
    {
        public async Task AddMatchAsync(MatchEntity match, CancellationToken ct)
            => await context.Matches.AddAsync(match, ct);

        public async Task<MatchEntity?> GetMatchAsync(Guid matchId, CancellationToken ct)
        {
            return await context.Matches
                .Include(m => m.Questions)
                .FirstOrDefaultAsync(m => m.Id == matchId, ct);
        }

        public async Task<MatchQuestionEntity?> GetMatchQuestionAsync(Guid matchId, int order, CancellationToken ct)
        {
            return await context.MatchQuestions
                .AsNoTracking()
                .Include(mq => mq.Question)
                    .ThenInclude(q => q.AnswerOptions)
                .FirstOrDefaultAsync(mq => mq.MatchId == matchId && mq.Order == order, ct);
        }

        public async Task<List<Guid>> GetExpiredMatchIdsAsync(DateTime nowUtc, CancellationToken ct)
        {
            return await context.Matches
                .Where(m => (m.Status == MatchStatus.QuestionActive || m.Status == MatchStatus.QuestionClosed)
                    && m.QuestionEndsAt <= nowUtc)
                .Select(m => m.Id)
                .ToListAsync(ct);
        }

        public async Task<bool> OptionBelongsToQuestionAsync(Guid questionId, Guid optionId, CancellationToken ct)
        {
            return await context.AnswerOptions
                .AnyAsync(o => o.Id == optionId && o.QuestionId == questionId, ct);
        }

        public async Task<Dictionary<Guid, int>> GetCorrectAnswerCountsAsync(Guid matchId, CancellationToken ct)
        {
            return await context.PlayerAnswers
                .Where(a => a.MatchQuestion.MatchId == matchId
                    && a.SelectedOption.IsCorrect
                    && a.SelectedOption.QuestionId == a.MatchQuestion.QuestionId)
                .GroupBy(a => a.PlayerId)
                .Select(g => new { PlayerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.PlayerId, x => x.Count, ct);
        }

        public async Task AddAnswerAsync(PlayerAnswerEntity answer, CancellationToken ct)
            => await context.PlayerAnswers.AddAsync(answer, ct);

        public async Task SaveChangesAsync(CancellationToken ct)
            => await context.SaveChangesAsync(ct);

        public async Task<MatchEntity?> GetMatchByRoomId(Guid roomId, CancellationToken ct)
        {
            return await context.Matches.FirstOrDefaultAsync(match => match.RoomId == roomId, ct);
        }
    }
}
