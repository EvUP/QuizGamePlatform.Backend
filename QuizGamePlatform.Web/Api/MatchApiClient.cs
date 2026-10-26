using QuizGamePlatform.Web.Api.Contracts;

namespace QuizGamePlatform.Web.Api;

/// <summary>Матч: /api/Match</summary>
public sealed class MatchApiClient(HttpClient http) : ApiClientBase(http)
{
    private static ApiJsonContext Json => ApiJsonContext.Default;

    public Task<ApiResult<MatchStateResponse>> StartAsync(StartMatchRequest request, CancellationToken ct = default)
        => PostAsync("api/Match/start", request, Json.StartMatchRequest, Json.MatchStateResponse, ct);

    public Task<ApiResult<CurrentQuestionResponse>> GetCurrentQuestionAsync(Guid matchId, CancellationToken ct = default)
        => GetAsync($"api/Match/{matchId}/current-question", Json.CurrentQuestionResponse, ct);

    // 409 до финала, 404 не участнику
    public Task<ApiResult<MatchReviewResponse>> GetReviewAsync(Guid matchId, CancellationToken ct = default)
        => GetAsync($"api/Match/{matchId}/review", Json.MatchReviewResponse, ct);

    // 400 «Ответ не принят»: второй раз, после дедлайна, вышедший игрок
    public Task<ApiResult> AnswerAsync(Guid matchId, Guid optionId, CancellationToken ct = default)
        => PostAsync($"api/Match/{matchId}/answer", new SubmitAnswerRequest(optionId), Json.SubmitAnswerRequest, ct);

    public Task<ApiResult<MatchStateResponse>> CloseQuestionAsync(Guid matchId, CancellationToken ct = default)
        => PostAsync($"api/Match/{matchId}/close-question", Json.MatchStateResponse, ct);

    public Task<ApiResult<MatchStateResponse>> NextQuestionAsync(Guid matchId, CancellationToken ct = default)
        => PostAsync($"api/Match/{matchId}/next", Json.MatchStateResponse, ct);
}
