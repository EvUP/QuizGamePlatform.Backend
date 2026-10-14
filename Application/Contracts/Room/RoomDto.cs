using QuizGamePlatform.Backend.Application.Contracts.Enums;
using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.Application.Contracts.Room
{
    // без тела обычная комната; командная без списка команд это две команды по умолчанию
    public record CreateRoomRequest(bool IsTeamGame, List<NewTeamRequest>? Teams = null);

    // пустое название заменяется на «Команда N»
    public record NewTeamRequest(string? Name, int? ExpectedPlayers = null);

    public record CreateRoomResponse(
        Guid Id,
        string RoomCode,
        RoomStatus Status,
        DateTime CreatedAt,
        bool IsTeamGame,
        string? JoinLink,                // ссылка обычной комнаты, в командную входят по ссылкам команд
        List<TeamLinkResponse>? Teams);

    public record TeamLinkResponse(int Number, string Name, int? ExpectedPlayers, string JoinLink);

    // RoomCode это код из ссылки: комнаты или команды; аккаунт входит под логином, ник нужен только гостю
    public record JoinToRoomRequest(string? Username, string RoomCode);

    public record ChangeTeamRequest(int TeamNumber);

    public record TeamNameRequest(string? Name);

    public record RoomResponse(
    Guid RoomPlayerLinkId,   // Это Id самой связи (RoomPlayerEntity.Id)
    Guid RoomId,             // ID комнаты
    string RoomCode,          // Код комнаты (удобно для UI)
    RoomStatus RoomStatus,   // Текущий статус комнаты
    string PlayerName,        // Ник в этой комнате
    DateTime? FinishedAt,
    DateTime JoinedAt,        // Время присоединения
    bool IsActive,            // Активен ли игрок в комнате
    int? TeamNumber);

    public record RoomParticipationResponse(
    Guid RoomPlayerLinkId,
    string PlayerName,        // у аккаунта это логин
    bool IsGuest,
    bool IsHost,
    bool IsActive,
    ExitReason? ExitReason,
    int? Score,               // в командной игре не показываем

    int? TeamNumber,
    bool IsCaptain
    );

    // очки и место только после матча
    public record TeamResponse(
        int Number,
        string Name,
        int Members,          // в лобби активные, после старта состав на старте
        int? ExpectedPlayers,
        Guid? CaptainLinkId,
        string? JoinLink,     // только владельцу
        int? Points,          // до 100 за вопрос
        int? Place);

    // состояние комнаты для её участников, игроки по очкам
    public record RoomStateResponse(
        Guid Id,
        string RoomCode,
        RoomStatus Status,
        Guid? MatchId,
        Guid MyRoomPlayerLinkId,
        List<RoomParticipationResponse> Players,
        bool IsTeamGame,
        List<TeamResponse>? Teams);

    // общий лидерборд, только аккаунты
    public record GlobalLeaderboardEntry(
        string UserName,
        int Score,
        int MatchesPlayed,
        int AccuracyPercent);   // верные от всех вопросов сыгранных матчей

    public record JoinToRoomResponse(
        RoomResponse Room,
        string? GuestAccessToken,
        DateTime? GuestTokenExpiresAtUtc);

    public record LeaveRoomResponse(
        Guid RoomId,
        Guid RoomPlayerLinkId,
        DateTime FinishedAt,
        ExitReason ExitReason
    );

    // участника определяем по токену
    public record LeaveRoomRequest(
        Guid RoomId,
        ExitReason ExitReason
    );
}