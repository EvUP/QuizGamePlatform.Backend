using QuizGamePlatform.Backend.Application.Contracts.Match;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Core.Helpers;
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
                TotalQuestions: match.Questions.Count,
                QuestionEndsAt: match.QuestionEndsAt);
        }

        // варианты и ответы вопроса должны быть загружены вместе с ним
        public static QuestionReview ToQuestionReview(this MatchQuestionEntity matchQuestion, Guid myRoomPlayerId)
        {
            var options = matchQuestion.Question.AnswerOptions.OrderBy(o => o.Position).ToList();
            var correctIds = options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();

            return new QuestionReview(
                Order: matchQuestion.Order,
                Text: matchQuestion.Question.Text,
                Options: options.Select(o => new AnswerOptionView(o.Id, o.Text, o.Position)).ToList(),
                CorrectOptionId: options.FirstOrDefault(o => o.IsCorrect)?.Id,
                MyAnswerOptionId: matchQuestion.Answers.FirstOrDefault(a => a.RoomPlayerId == myRoomPlayerId)?.SelectedOptionId,
                AnsweredCount: matchQuestion.Answers.Count,
                CorrectCount: matchQuestion.Answers.Count(a => correctIds.Contains(a.SelectedOptionId)));
        }

        // ответы вопроса должны быть загружены вместе с ним
        public static CurrentQuestionResponse ToCurrentQuestionResponse(
            this MatchQuestionEntity matchQuestion,
            MatchEntity match,
            Guid myRoomPlayerId,
            DateTime nowUtc)
        {
            return new CurrentQuestionResponse(
                MatchId: match.Id,
                Status: match.Status,
                QuestionOrder: matchQuestion.Order,
                TotalQuestions: match.Questions.Count,
                QuestionEndsAt: match.QuestionEndsAt,
                StageSeconds: match.Status switch
                {
                    MatchStatus.QuestionActive => MatchHelper.GetQuestionDurationByRoomMode(match.RoomMode),
                    MatchStatus.QuestionClosed => MatchHelper.RevealSeconds,
                    _ => 0
                },
                ServerTimeUtc: nowUtc,
                QuestionId: matchQuestion.QuestionId,
                Text: matchQuestion.Question.Text,
                Options: matchQuestion.Question.AnswerOptions
                    .OrderBy(o => o.Position)
                    .Select(o => new AnswerOptionView(o.Id, o.Text, o.Position))
                    .ToList(),
                // пока вопрос идёт, ответ не отдаём
                CorrectOptionId: match.Status == MatchStatus.QuestionActive
                    ? null
                    : matchQuestion.Question.AnswerOptions.FirstOrDefault(o => o.IsCorrect)?.Id,
                MyAnswerOptionId: matchQuestion.Answers.FirstOrDefault(a => a.RoomPlayerId == myRoomPlayerId)?.SelectedOptionId,
                AnsweredLinkIds: matchQuestion.Answers.Select(a => a.RoomPlayerId).ToList());
        }
    }
}
