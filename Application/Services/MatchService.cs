using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts.Match;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Application.Mappers;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class MatchService(
        IMatchRepository matchRepository,
        IRoomRepository roomRepository,
        IQuizContentRepository quizContentRepository,
        ILogger<MatchService> logger) : IMatchService
    {
        public async Task<MatchStateResponse?> StartMatchAsync(StartMatchRequest request, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByIdAsync(request.RoomId, ct);

            if (room is null || room.Status != RoomStatus.Waiting)
            {
                logger.LogInformation("Cannot start match: room {RoomId} not found or not waiting", request.RoomId);

                return null;
            }

            var questions = await quizContentRepository.GetQuestionsByCategoryAsync(request.CategoryId, ct);

            var selected = questions?.Take(request.QuestionCount).ToList() ?? [];

            if (selected.Count == 0)
            {
                logger.LogInformation("Cannot start match: no questions for category {CategoryId}", request.CategoryId);

                return null;
            }

            var match = new MatchEntity
            {
                Id = Guid.NewGuid(),
                RoomId = room.Id,
                Status = MatchStatus.QuestionActive,
                CurrentQuestionIndex = 0,
                StartedAt = DateTime.UtcNow,
                Questions = selected
                    .Select((question, index) => new MatchQuestionEntity
                    {
                        Id = Guid.NewGuid(),
                        QuestionId = question.Id,
                        Order = index
                    })
                    .ToList()
            };

            room.Status = RoomStatus.InProgress;

            await matchRepository.AddMatchAsync(match, ct);
            await matchRepository.SaveChangesAsync(ct);

            logger.LogInformation("Match {MatchId} started in room {RoomId} with {Count} questions", match.Id, room.Id, match.Questions.Count);

            return match.ToStateResponse();
        }

        public async Task<CurrentQuestionResponse?> GetCurrentQuestionAsync(Guid matchId, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchAsync(matchId, ct);

            if (match is null)
            {
                return null;
            }

            var currentQuestion = await matchRepository.GetMatchQuestionAsync(matchId, match.CurrentQuestionIndex, ct);

            if (currentQuestion is null)
            {
                return null;
            }

            return currentQuestion.ToCurrentQuestionResponse(match);
        }

        public async Task<bool> SubmitAnswerAsync(Guid matchId, SubmitAnswerRequest request, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchAsync(matchId, ct);

            if (match is null || match.Status != MatchStatus.QuestionActive)
            {
                return false;
            }

            var currentQuestion = match.Questions.FirstOrDefault(q => q.Order == match.CurrentQuestionIndex);

            if (currentQuestion is null)
            {
                return false;
            }

            var answer = new PlayerAnswerEntity
            {
                Id = Guid.NewGuid(),
                MatchQuestionId = currentQuestion.Id,
                PlayerId = request.PlayerId,
                SelectedOptionId = request.SelectedOptionId,
                AnsweredAt = DateTime.UtcNow
            };

            await matchRepository.AddAnswerAsync(answer, ct);
            await matchRepository.SaveChangesAsync(ct);

            logger.LogInformation("Player {PlayerId} answered question {Order} of match {MatchId}", request.PlayerId, currentQuestion.Order, matchId);

            return true;
        }

        public async Task<MatchStateResponse?> CloseQuestionAsync(Guid matchId, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchAsync(matchId, ct);

            if (match is null || match.Status != MatchStatus.QuestionActive)
            {
                return null;
            }

            match.Status = MatchStatus.QuestionClosed;

            await matchRepository.SaveChangesAsync(ct);

            logger.LogInformation("Question {Order} of match {MatchId} closed", match.CurrentQuestionIndex, matchId);

            return match.ToStateResponse();
        }

        public async Task<MatchStateResponse?> NextQuestionAsync(Guid matchId, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchAsync(matchId, ct);

            if (match is null || match.Status != MatchStatus.QuestionClosed)
            {
                return null;
            }

            if (match.CurrentQuestionIndex + 1 < match.Questions.Count)
            {
                match.CurrentQuestionIndex++;
                match.Status = MatchStatus.QuestionActive;
            }
            else
            {
                match.Status = MatchStatus.Finished;
                match.FinishedAt = DateTime.UtcNow;

                var room = await roomRepository.GetRoomByIdAsync(match.RoomId, ct);

                if (room is not null)
                {
                    room.Status = RoomStatus.Finished;
                }
            }

            await matchRepository.SaveChangesAsync(ct);

            logger.LogInformation("Match {MatchId} advanced to status {Status}, question {Order}", matchId, match.Status, match.CurrentQuestionIndex);

            return match.ToStateResponse();
        }
    }
}
