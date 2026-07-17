using QuizGamePlatform.Backend.Application.Contracts.Match;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Application.Mappers
{
    public static class MatchMapper
    {
        public static MatchStateResponse ToStateResponse(this MatchEntity match)
        {
            return new MatchStateResponse(
                MatchId: match.Id,
                RoomId: match.RoomId,
                Status: match.Status,
                CurrentQuestionIndex: match.CurrentQuestionIndex,
                TotalQuestions: match.Questions.Count);
        }

        public static CurrentQuestionResponse ToCurrentQuestionResponse(this MatchQuestionEntity matchQuestion, MatchEntity match)
        {
            return new CurrentQuestionResponse(
                MatchId: match.Id,
                Status: match.Status,
                QuestionOrder: matchQuestion.Order,
                TotalQuestions: match.Questions.Count,
                QuestionId: matchQuestion.QuestionId,
                Text: matchQuestion.Question.Text,
                Options: matchQuestion.Question.AnswerOptions
                    .OrderBy(o => o.Position)
                    .Select(o => new AnswerOptionView(o.Id, o.Text, o.Position))
                    .ToList());
        }
    }
}
