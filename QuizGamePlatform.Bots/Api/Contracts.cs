namespace QuizGamePlatform.Bots.Api;

// только поля, которые нужны ботам; имена и enum'ы строками, как отдаёт API

public enum RoomStatus { Waiting, InProgress, Finished }

public enum MatchStatus { QuestionActive, QuestionClosed, Finished }

public enum ExitReason { Normal, RoomFinished, Disconnected, Cheating, Other, Kicked }

public enum RoomMode { Simple, Fastest }

public record CreateRoomRequest(bool IsTeamGame, List<NewTeamRequest>? Teams = null);

public record NewTeamRequest(string? Name, int? ExpectedPlayers = null);

public record CreateRoomResponse(Guid Id, string RoomCode, bool IsTeamGame, string? JoinLink, List<TeamLink>? Teams);

public record TeamLink(int Number, string Name, string? JoinLink);

// что видно по ссылке до входа: какая комната и чья команда
public record Invite(bool IsTeamGame, RoomStatus Status, string? HostName, int? TeamNumber, string? TeamName);

// «Мои комнаты»: ссылки команд видит только владелец
public record MyRoom(Guid Id, string RoomCode, RoomStatus Status, DateTime CreatedAt, bool IsTeamGame, string? JoinLink, List<TeamLink>? Teams);

public record JoinRequest(string? Username, string RoomCode);

public record JoinResponse(JoinedRoom Room, string? GuestAccessToken);

public record JoinedRoom(Guid RoomPlayerLinkId, Guid RoomId, string RoomCode, string PlayerName, int? TeamNumber);

public record RoomState(
    Guid Id,
    string RoomCode,
    RoomStatus Status,
    Guid? MatchId,
    Guid MyRoomPlayerLinkId,
    List<Participant> Players,
    bool IsTeamGame,
    List<Team>? Teams,
    int GameNumber = 1,
    // последняя игра серии после «Сыграть ещё», только на итогах
    Guid? NextRoomId = null,
    RoomStatus? NextRoomStatus = null);

public record Participant(
    Guid RoomPlayerLinkId,
    string PlayerName,
    bool IsGuest,
    bool IsHost,
    bool IsActive,
    ExitReason? ExitReason,
    int? Score,
    int? TeamNumber,
    int? Place);

public record Team(int Number, string Name, int Members, int? Points, int? Place);

public record Category(Guid Id, string Name);

public record StartMatchRequest(Guid RoomId, List<Guid> CategoryIds, int QuestionCount, RoomMode RoomMode);

public record CurrentQuestion(
    Guid MatchId,
    MatchStatus Status,
    int QuestionOrder,
    int TotalQuestions,
    DateTime QuestionEndsAt,
    DateTime ServerTimeUtc,
    string Text,
    List<AnswerOption> Options,
    Guid? MyAnswerOptionId,
    List<Guid> AnsweredLinkIds);

public record AnswerOption(Guid Id, string Text, int Position);

public record SubmitAnswerRequest(Guid SelectedOptionId);

public record LeaveRequest(Guid RoomId, ExitReason ExitReason);

public record ErrorResponse(string? Message);
