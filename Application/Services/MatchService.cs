using Microsoft.EntityFrameworkCore;
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
        IRoomParticipationRepository roomParticipationRepository,
        IQuizContentRepository quizContentRepository,
        IMatchLockProvider matchLocks,
        ILogger<MatchService> logger) : IMatchService
    {
        private const int QuestionDurationSeconds = 30;

        public async Task<MatchStateResponse?> StartMatchAsync(StartMatchRequest request, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByIdAsync(request.RoomId, ct);

            if (room is null || room.Status != RoomStatus.Waiting)
            {
                logger.LogInformation("Cannot start match: room {RoomId} not found or not waiting", request.RoomId);

                return null;
            }

            var activePlayers = await roomParticipationRepository.CountActivePlayers(request.RoomId, ct);

            if (activePlayers < 2)
            {
                logger.LogInformation("Cannot start match: room {RoomId} has no active players", request.RoomId);

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
                QuestionEndsAt = DateTime.UtcNow.AddSeconds(QuestionDurationSeconds),
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

            try
            {
                await matchRepository.AddMatchAsync(match, ct);
                await matchRepository.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                logger.LogInformation("Cannot start match: room {RoomId} already has an active match", room.Id);

                return null;
            }

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

            if (match is null || match.Status != MatchStatus.QuestionActive || DateTime.UtcNow > match.QuestionEndsAt)
            {
                return false;
            }

            var currentQuestion = match.Questions.FirstOrDefault(q => q.Order == match.CurrentQuestionIndex);

            if (currentQuestion is null)
            {
                return false;
            }
            
            if (!await matchRepository.OptionBelongsToQuestionAsync(currentQuestion.QuestionId, request.SelectedOptionId, ct))
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

            try
            {
                await matchRepository.AddAnswerAsync(answer, ct);
                await matchRepository.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return false;
            }

            logger.LogInformation("Player {PlayerId} answered question {Order} of match {MatchId}", request.PlayerId, currentQuestion.Order, matchId);

            return true;
        }

        public async Task<MatchStateResponse?> CloseQuestionAsync(Guid matchId, CancellationToken ct)
        {
            return await RunExclusiveAsync(matchId, async () =>
            {
                var match = await matchRepository.GetMatchAsync(matchId, ct);

                if (match is null || match.Status != MatchStatus.QuestionActive)
                {
                    return null;
                }

                match.Status = MatchStatus.QuestionClosed;
                match.QuestionEndsAt = DateTime.UtcNow.AddSeconds(QuestionDurationSeconds);

                await matchRepository.SaveChangesAsync(ct);

                logger.LogInformation("Question {Order} of match {MatchId} closed", match.CurrentQuestionIndex, matchId);

                return match.ToStateResponse();
            }, ct);
        }

        public async Task<MatchStateResponse?> NextQuestionAsync(Guid matchId, CancellationToken ct)
        {
            return await RunExclusiveAsync(matchId, async () =>
            {
                var match = await matchRepository.GetMatchAsync(matchId, ct);

                if (match is null || match.Status != MatchStatus.QuestionClosed)
                {
                    return null;
                }

                await AdvanceMatchAsync(match, ct);

                await matchRepository.SaveChangesAsync(ct);

                logger.LogInformation("Match {MatchId} advanced to status {Status}, question {Order}", matchId, match.Status, match.CurrentQuestionIndex);

                return match.ToStateResponse();
            }, ct);
        }

        public async Task AdvanceExpiredMatchesAsync(CancellationToken ct)
        {
            var expiredIds = await matchRepository.GetExpiredMatchIdsAsync(DateTime.UtcNow, ct);

            foreach (var matchId in expiredIds)
            {
                await RunExclusiveAsync(matchId, () => AdvanceIfStillExpiredAsync(matchId, ct), ct);
            }
        }

        // под локом перепроверяем вдруг матч уже сдвинули руками
        private async Task<bool> AdvanceIfStillExpiredAsync(Guid matchId, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchAsync(matchId, ct);

            var isRunning = match?.Status is MatchStatus.QuestionActive or MatchStatus.QuestionClosed;

            if (match is null || !isRunning || DateTime.UtcNow <= match.QuestionEndsAt)
            {
                return false;
            }

            await AdvanceMatchAsync(match, ct);

            await matchRepository.SaveChangesAsync(ct);

            logger.LogInformation("Match {MatchId} auto-advanced to status {Status}, question {Order}", match.Id, match.Status, match.CurrentQuestionIndex);

            return true;
        }

        private async Task AdvanceMatchAsync(MatchEntity match, CancellationToken ct)
        {
            if (match.CurrentQuestionIndex + 1 < match.Questions.Count)
            {
                match.CurrentQuestionIndex++;
                match.Status = MatchStatus.QuestionActive;
                match.QuestionEndsAt = DateTime.UtcNow.AddSeconds(QuestionDurationSeconds);

                return;
            }

            match.Status = MatchStatus.Finished;
            match.FinishedAt = DateTime.UtcNow;

            await ApplyFinalScoresAsync(match, ct);

            var room = await roomRepository.GetRoomByIdAsync(match.RoomId, ct);

            if (room is not null)
            {
                room.Status = RoomStatus.Finished;
            }
        }

        private async Task ApplyFinalScoresAsync(MatchEntity match, CancellationToken ct)
        {
            var correctCounts = await matchRepository.GetCorrectAnswerCountsAsync(match.Id, ct);

            var participants = await roomParticipationRepository.GetParticipationsByRoomId(match.RoomId, ct);

            foreach (var participant in participants)
            {
                participant.Score = participant.IsActive
                    ? correctCounts.GetValueOrDefault(participant.PlayerId)
                    : 0;
            }
        }


        private async Task<T> RunExclusiveAsync<T>(Guid matchId, Func<Task<T>> action, CancellationToken ct)
        {
            var gate = matchLocks.GetLock(matchId);

            await gate.WaitAsync(ct);

            try
            {
                return await action();
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
