using QuizGamePlatform.Backend.Application.Enums;

namespace QuizGamePlatform.Backend.Application.Contracts.Match
{
    public record StartMatchRequest(Guid RoomId, Guid CategoryId, int QuestionCount);

    public record SubmitAnswerRequest(Guid PlayerId, Guid SelectedOptionId);

    public record MatchStateResponse(
        Guid MatchId,
        Guid RoomId,
        MatchStatus Status,
        int CurrentQuestionIndex,
        int TotalQuestions);

    // Текущий вопрос для игроков — БЕЗ признака правильного ответа
    public record CurrentQuestionResponse(
        Guid MatchId,
        MatchStatus Status,
        int QuestionOrder,
        int TotalQuestions,
        Guid QuestionId,
        string Text,
        List<AnswerOptionView> Options);

    public record AnswerOptionView(Guid Id, string Text, int Position);
}
