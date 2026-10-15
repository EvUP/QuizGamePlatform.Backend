namespace QuizGamePlatform.Web.Api.Contracts;

// Копия контрактов бэка: Application/Contracts/Match/MatchDto.cs

// вопросы случайно из всех выбранных тем, без повторов
public record StartMatchRequest(
    Guid RoomId,
    List<Guid> CategoryIds,
    int QuestionCount,
    RoomMode RoomMode);

public record SubmitAnswerRequest(Guid SelectedOptionId);

public record MatchStateResponse(
    Guid MatchId,
    Guid RoomId,
    MatchStatus Status,
    int CurrentQuestionIndex,
    int TotalQuestions,
    DateTime QuestionEndsAt);

// кто ответил, без самого ответа (событие хаба PlayerAnswered)
public record PlayerAnsweredEvent(
    Guid RoomId,
    Guid MatchId,
    int QuestionIndex,
    Guid RoomPlayerLinkId);

// правильный вариант только после закрытия вопроса; QuestionEndsAt в показе ответа — конец паузы
public record CurrentQuestionResponse(
    Guid MatchId,
    MatchStatus Status,
    int QuestionOrder,
    int TotalQuestions,
    DateTime QuestionEndsAt,
    int StageSeconds,              // полная длительность стадии: вопроса по режиму или показа ответа
    DateTime ServerTimeUtc,        // для сверки часов (ServerClock)
    Guid QuestionId,
    string Text,
    List<AnswerOptionView> Options,
    Guid? CorrectOptionId,
    Guid? MyAnswerOptionId,
    List<Guid> AnsweredLinkIds);

public record AnswerOptionView(Guid Id, string Text, int Position);

// разбор после финала: GET /api/Match/{matchId}/review, до финала 409
public record MatchReviewResponse(Guid MatchId, List<QuestionReview> Questions);

public record QuestionReview(
    int Order,                     // с нуля, как QuestionOrder
    string Text,
    List<AnswerOptionView> Options,
    Guid? CorrectOptionId,
    Guid? MyAnswerOptionId,        // null — не отвечали
    int AnsweredCount,
    int CorrectCount);
