namespace QuizGamePlatform.Web.Api.Contracts;

public record CategoryDto(Guid Id, string Name);

// общий лидерборд, только аккаунты; сервер сортирует по очкам
public record GlobalLeaderboardEntry(
    string UserName,
    int Score,
    int MatchesPlayed,
    int AccuracyPercent);

public enum LeaderboardKind
{
    Regular,    // обычные игры
    TeamGames   // личные очки аккаунтов в командных играх
}

/// <summary>Ответ GET /api/Auth/me: аккаунт или гостевое участие</summary>
public record CurrentUserResponse(
    string Kind,
    string? KeycloakSubject,
    string? DisplayName,
    string? Email,
    Guid? RoomPlayerLinkId,
    Guid? RoomId,
    string? Nickname)
{
    public const string RegisteredKind = "registered";
    public const string GuestKind = "guest";

    public bool IsGuest => Kind == GuestKind;
}

// тело ошибок бэка: тексты по-русски, можно показывать как есть
public record CommonErrorResponse(string? Message, string? Method);
