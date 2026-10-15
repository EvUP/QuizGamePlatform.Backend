using QuizGamePlatform.Backend.Application.Contracts.Match;
using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.Application.Abstractions
{
    public interface IMatchService
    {
        Task<bool> IsRoomHostAsync(Guid roomId, CancellationToken ct);
        Task<bool> IsMatchHostAsync(Guid matchId, CancellationToken ct);
        Task<(MatchStateResponse? Match, StartRefusal Refusal)> StartMatchAsync(StartMatchRequest request, CancellationToken ct);
        Task<CurrentQuestionResponse?> GetCurrentQuestionAsync(Guid matchId, CancellationToken ct);
        Task<bool> SubmitAnswerAsync(Guid matchId, SubmitAnswerRequest request, CancellationToken ct);
        Task<MatchStateResponse?> CloseQuestionAsync(Guid matchId, CancellationToken ct);
        Task<MatchStateResponse?> NextQuestionAsync(Guid matchId, CancellationToken ct);
        Task<List<Guid>> GetExpiredMatchIdsAsync(CancellationToken ct);
        Task<bool> AdvanceExpiredMatchAsync(Guid matchId, CancellationToken ct);
    }
}
