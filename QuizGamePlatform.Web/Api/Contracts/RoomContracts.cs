namespace QuizGamePlatform.Web.Api.Contracts;

// Копия контрактов бэка: Application/Contracts/Room/RoomDto.cs

// без команд командная комната получает две команды по умолчанию
public record CreateRoomRequest(bool IsTeamGame, List<NewTeamRequest>? Teams = null);

// пустое название сервер заменяет на «Команда N»
public record NewTeamRequest(string? Name, int? ExpectedPlayers = null);

public record CreateRoomResponse(
    Guid Id,
    string RoomCode,
    RoomStatus Status,
    DateTime CreatedAt,
    bool IsTeamGame,
    string? JoinLink,                  // ссылка обычной комнаты
    List<TeamLinkResponse>? Teams);    // в командную входят по ссылкам команд

// ссылку сервер отдаёт только владельцу комнаты
public record TeamLinkResponse(int Number, string Name, int? ExpectedPlayers, string? JoinLink);

// строка «Мои комнаты»: GET /api/Room/my
public record MyRoomResponse(
    Guid Id,
    string RoomCode,
    RoomStatus Status,
    DateTime CreatedAt,
    bool IsTeamGame,
    string? JoinLink,
    List<TeamLinkResponse>? Teams,
    MyRoomResult? Result,              // только у завершённых (сервер считает для последних 20)
    int GameNumber = 1,                // номер игры в серии «Сыграть ещё»
    Guid? SeriesId = null,             // сервер ставит SeriesId ?? Id, по нему серия сворачивается в одну строку
    bool IsHost = false,
    string? HostName = null,
    int? MyTeamNumber = null,          // название из Teams
    bool IsActive = true,              // false: вышел или убран, причина в MyExitReason
    ExitReason? MyExitReason = null);

// места как на экране итогов, без дележа
public record MyRoomResult(
    int? MyPlace,                      // в командной место моей команды; null, если ушли до старта
    int? MyPoints,                     // в обычной верные ответы, в командной очки команды
    string? WinnerName,                // первое место: игрок или команда; null при ничьей
    bool IsDraw = false);              // лидеры равны по очкам и времени верных, победителя нет

// превью приглашения до входа: GET /api/Room/invite/{code}
public record InviteResponse(
    bool IsTeamGame,
    RoomStatus Status,
    string? HostName,
    int? TeamNumber,           // только у ссылки команды
    string? TeamName,
    int GameNumber = 1);

// RoomCode: последний сегмент ссылки; ник нужен только гостю
public record JoinToRoomRequest(string? Username, string RoomCode);

public record JoinToRoomResponse(
    RoomResponse Room,
    string? GuestAccessToken,
    DateTime? GuestTokenExpiresAtUtc);

public record RoomResponse(
    Guid RoomPlayerLinkId,
    Guid RoomId,
    string RoomCode,
    RoomStatus RoomStatus,
    string PlayerName,
    DateTime? FinishedAt,
    DateTime JoinedAt,
    bool IsActive,
    int? TeamNumber);

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
    Guid? NextRoomId = null,               // последняя игра серии, только у Finished и если её номер больше
    int? NextGameNumber = null,
    RoomStatus? NextRoomStatus = null,
    int PreviousPlayers = 0,               // только ведущему в лобби игры N>1
    int ReturnedPlayers = 0,               // то же
    PreviousMatchSettings? PreviousSettings = null, // то же; null, если матч прошлой игры старше миграции
    bool MyCanReturn = false,          // я вышел, но сервер пустит обратно: лобби или окно возврата в матч
    bool IsDraw = false);              // только на итогах: у двух первых поровну очков и времени, победителя нет

// настройки матча прошлой игры серии подставляются в лобби следующей
public record PreviousMatchSettings(
    List<Guid> CategoryIds,
    int QuestionCount,
    RoomMode Mode);

public record RoomParticipationResponse(
    Guid RoomPlayerLinkId,
    string PlayerName,
    bool IsGuest,
    bool IsHost,
    bool IsActive,
    ExitReason? ExitReason,
    int? Score,                // в командной игре всегда null
    int? TeamNumber,
    bool IsCaptain,
    int? Place,                // только в итогах обычной игры, места без повторов
    long? CorrectTimeMs);      // сумма времени верных ответов: по ней разбиты равные очки

public record TeamResponse(
    int Number,
    string Name,
    int Members,
    int? ExpectedPlayers,
    Guid? CaptainLinkId,
    string? JoinLink,          // только ведущему
    int? Points,               // null в лобби, в игре по закрытым вопросам
    int? Place,                // без повторов: равные очки разбиты по среднему времени
    long? AvgCorrectTimeMs);   // среднее время верного ответа

public record LeaveRoomRequest(Guid RoomId, ExitReason ExitReason);

public record LeaveRoomResponse(
    Guid RoomId,
    Guid RoomPlayerLinkId,
    DateTime FinishedAt,
    ExitReason ExitReason);

public record ChangeTeamRequest(int TeamNumber);

public record TeamNameRequest(string? Name);
