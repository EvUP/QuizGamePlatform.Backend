using QuizGamePlatform.Web.Api.Contracts;

namespace QuizGamePlatform.Web.Api;

/// <summary>Рейтинги, без авторизации</summary>
public sealed class LeaderboardApiClient(HttpClient http) : ApiClientBase(http)
{
    public Task<ApiResult<List<GlobalLeaderboardEntry>>> GetAsync(LeaderboardKind kind, CancellationToken ct = default)
        => GetAsync(
            kind == LeaderboardKind.TeamGames ? "api/Room/leaderboard/team-games" : "api/Room/leaderboard",
            ApiJsonContext.Default.ListGlobalLeaderboardEntry,
            ct);
}

/// <summary>Темы вопросов, без авторизации</summary>
public sealed class CategoriesApiClient(HttpClient http) : ApiClientBase(http)
{
    public Task<ApiResult<List<CategoryDto>>> GetAllAsync(CancellationToken ct = default)
        => GetAsync("api/Categories", ApiJsonContext.Default.ListCategoryDto, ct);
}

/// <summary>Кто я по токену: аккаунт или гость</summary>
public sealed class AuthApiClient(HttpClient http) : ApiClientBase(http)
{
    public Task<ApiResult<CurrentUserResponse>> GetMeAsync(CancellationToken ct = default)
        => GetAsync("api/Auth/me", ApiJsonContext.Default.CurrentUserResponse, ct);
}
