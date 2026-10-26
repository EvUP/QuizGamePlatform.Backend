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

    // ссылку видит только владелец комнаты
    public record TeamLinkResponse(int Number, string Name, int? ExpectedPlayers, string? JoinLink);

    // строка «Мои комнаты»: то же, что при создании, и итог завершённой игры
    public record MyRoomResponse(
        Guid Id,
        string RoomCode,
        RoomStatus Status,
        DateTime CreatedAt,
        bool IsTeamGame,
        string? JoinLink,
        List<TeamLinkResponse>? Teams,
        MyRoomResult? Result,            // только у завершённых и только у последних из них
        int GameNumber = 1,
        Guid? SeriesId = null,           // Id первой игры серии, у одиночной своя Id
        bool IsHost = false,
        string? HostName = null,
        int? MyTeamNumber = null,        // название берётся из Teams
        bool IsActive = true,            // false: вышел или убран, причина в MyExitReason
        ExitReason? MyExitReason = null);

    // места как на экране итогов, без дележа
    public record MyRoomResult(
        int? MyPlace,                    // в командной у моей команды; null если я ушёл до старта
        int? MyPoints,                   // в обычной мои верные ответы, в командной очки моей команды
        string? WinnerName,              // первое место: игрок или команда; null при ничьей
        bool IsDraw = false);            // лидеры равны по очкам и времени верных, победителя нет

    // что видно по ссылке до входа, код комнаты и состав не раскрываем
    public record InviteResponse(
        bool IsTeamGame,
        RoomStatus Status,
        string? HostName,
        int? TeamNumber,       // только у ссылки команды
        string? TeamName,
        int GameNumber = 1);

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
    bool IsCaptain,
    int? Place,               // только в итогах обычной игры, без дележа мест
    long? CorrectTimeMs       // сумма времени верных ответов, по ней разбиваем равные очки
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
        int? Place,           // без дележа: при равных очках по среднему времени верных
        long? AvgCorrectTimeMs); // среднее время верного ответа, по нему разбиваем равные очки

    // состояние комнаты для её участников, игроки по очкам
    public record RoomStateResponse(
        Guid Id,
        string RoomCode,
        RoomStatus Status,
        Guid? MatchId,
        Guid MyRoomPlayerLinkId,
        List<RoomParticipationResponse> Players,
        bool IsTeamGame,
        List<TeamResponse>? Teams,
        int GameNumber = 1,
        Guid? PreviousRoomId = null,
        Guid? NextRoomId = null,              // последняя игра серии, только на итогах и если она новее этой
        int? NextGameNumber = null,
        RoomStatus? NextRoomStatus = null,
        int PreviousPlayers = 0,              // только ведущему в лобби игры N>1: активные в прошлой игре без него
        int ReturnedPlayers = 0,              // то же: уже перешли из прошлой игры
        PreviousMatchSettings? PreviousSettings = null, // то же; null если матч прошлой игры старше этих настроек
        bool MyCanReturn = false,             // я вышел, но сервер пустит обратно: лобби или окно возврата в матч
        bool IsDraw = false);                 // только на итогах: у двух первых поровну очков и времени, победителя нет

    // с чем стартовала прошлая игра серии, ведущий может поменять
    public record PreviousMatchSettings(
        List<Guid> CategoryIds,
        int QuestionCount,
        RoomMode Mode);

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