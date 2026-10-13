using QuizGamePlatform.Backend.Application.Contracts.Enums;
using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.Application.Contracts.Room
{
    public record CreateRoomResponse(
        Guid Id,
        string RoomCode,
        RoomStatus Status,
        DateTime CreatedAt);

    // аккаунт входит под логином, ник нужен только гостю
    public record JoinToRoomRequest(string? Username, string RoomCode);

    public record RoomResponse(
    Guid RoomPlayerLinkId,   // Это Id самой связи (RoomPlayerEntity.Id)
    Guid RoomId,             // ID комнаты
    string RoomCode,          // Код комнаты (удобно для UI)
    RoomStatus RoomStatus,   // Текущий статус комнаты
    string PlayerName,        // Ник в этой комнате
    DateTime? FinishedAt,
    DateTime JoinedAt,        // Время присоединения
    bool IsActive);           // Активен ли игрок в комнате

    public record RoomParticipationResponse(
    Guid RoomPlayerLinkId,
    string PlayerName,        // у аккаунта это логин
    bool IsGuest,
    bool IsHost,
    bool IsActive,
    ExitReason? ExitReason,
    int Score
    );

    // состояние комнаты для её участников, игроки по очкам
    public record RoomStateResponse(
        Guid Id,
        string RoomCode,
        RoomStatus Status,
        Guid? MatchId,
        Guid MyRoomPlayerLinkId,
        List<RoomParticipationResponse> Players);

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