using System.ComponentModel.DataAnnotations;
using QuizGamePlatform.Backend.Application.Contracts.Enums;
using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.Application.Contracts.Match
{
    // вопросы случайно из всех выбранных тем, без повторов
    public record StartMatchRequest(
    Guid RoomId,
    List<Guid>? CategoryIds,
    int QuestionCount,
    [EnumDataType(typeof(RoomMode))] RoomMode RoomMode);

    // участника определяем по токену
    public record SubmitAnswerRequest(Guid SelectedOptionId);

    public record MatchStateResponse(
        Guid MatchId,
        Guid RoomId,
        MatchStatus Status,
        int CurrentQuestionIndex,
        int TotalQuestions,
        DateTime QuestionEndsAt);

    // кто ответил, без самого ответа
    public record PlayerAnsweredEvent(
        Guid RoomId,
        Guid MatchId,
        int QuestionIndex,
        Guid RoomPlayerLinkId);

    // Текущий вопрос для игроков; правильный вариант только после закрытия вопроса
    public record CurrentQuestionResponse(
        Guid MatchId,
        MatchStatus Status,
        int QuestionOrder,
        int TotalQuestions,
        DateTime QuestionEndsAt,
        int StageSeconds,              // полная длительность стадии: вопроса по режиму или показа ответа
        DateTime ServerTimeUtc,        // по нему клиент сверяет свои часы для таймера
        Guid QuestionId,
        string Text,
        List<AnswerOptionView> Options,
        Guid? CorrectOptionId,
        Guid? MyAnswerOptionId,        // свой ответ, чтобы после перезагрузки страницы он не пропал
        List<Guid> AnsweredLinkIds);   // кто уже ответил, без самих ответов

    public record AnswerOptionView(Guid Id, string Text, int Position);

    // разбор после финала: все вопросы игры по порядку
    public record MatchReviewResponse(Guid MatchId, List<QuestionReview> Questions);

    public record QuestionReview(
        int Order,                     // с нуля, как QuestionOrder
        string Text,
        List<AnswerOptionView> Options,
        Guid? CorrectOptionId,
        Guid? MyAnswerOptionId,        // null если не отвечал
        int AnsweredCount,             // сколько ответили
        int CorrectCount);             // из них верно
}
