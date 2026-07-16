using QuizGamePlatform.Backend.Application.Contracts.Match;

namespace QuizGamePlatform.Backend.Application.Abstractions
{
    public interface IMatchService
    {
        Task<MatchStateResponse?> StartMatchAsync(StartMatchRequest request, CancellationToken ct);
        Task<CurrentQuestionResponse?> GetCurrentQuestionAsync(Guid matchId, CancellationToken ct);
        Task<bool> SubmitAnswerAsync(Guid matchId, SubmitAnswerRequest request, CancellationToken ct);
        Task<MatchStateResponse?> CloseQuestionAsync(Guid matchId, CancellationToken ct);
        Task<MatchStateResponse?> NextQuestionAsync(Guid matchId, CancellationToken ct);
        Task AdvanceExpiredMatchesAsync(CancellationToken ct);
    }
}
