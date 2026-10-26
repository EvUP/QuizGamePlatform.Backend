using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts.Match;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Application.Mappers;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.Core.Helpers;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class MatchService(
        IMatchRepository matchRepository,
        IRoomRepository roomRepository,
        IRoomParticipationRepository roomParticipationRepository,
        IQuizContentRepository quizContentRepository,
        IMatchLockProvider matchLocks,
        ICurrentUserContext currentUser,
        IGameNotifier notifier,
        ILogger<MatchService> logger,
        TimeProvider timeProvider) : IMatchService
    {
        // столько раз повторяем старт, проигравший гонку за версию комнаты
        private const int StartAttempts = 5;

        private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

        public async Task<bool> IsRoomHostAsync(Guid roomId, CancellationToken ct)
            => currentUser.KeycloakSubject is { } subject
                && await roomRepository.IsHost(roomId, subject, ct);

        public async Task<bool> IsMatchHostAsync(Guid matchId, CancellationToken ct)
            => currentUser.KeycloakSubject is { } subject
                && await matchRepository.IsHost(matchId, subject, ct);

        public async Task<(MatchStateResponse? Match, StartRefusal Refusal)> StartMatchAsync(StartMatchRequest request, CancellationToken ct)
        {
            // в лобби игры N>1 игроки как раз переходят из прошлой игры и меняют версию комнаты: проигравший гонку старт повторяем
            for (var attempt = 1; ; attempt++)
            {
                var (match, refusal, conflict) = await TryStartMatchAsync(request, ct);

                if (!conflict)
                {
                    return (match, refusal);
                }

                if (attempt == StartAttempts)
                {
                    return (null, StartRefusal.Concurrent);
                }

                roomRepository.ForgetChanges();
                await Task.Delay(Random.Shared.Next(5, 50), ct);
            }
        }

        private async Task<(MatchStateResponse? Match, StartRefusal Refusal, bool Conflict)> TryStartMatchAsync(StartMatchRequest request, CancellationToken ct)
        {
            var room = await roomRepository.GetRoomByIdAsync(request.RoomId, ct);

            if (room is null || room.Status != RoomStatus.Waiting)
            {
                logger.LogInformation("Cannot start match: room {RoomId} not found or not waiting", request.RoomId);

                return (null, room?.Status switch
                {
                    null => StartRefusal.NotFound,
                    RoomStatus.InProgress => StartRefusal.Started,
                    _ => StartRefusal.Finished
                }, false);
            }

            var activePlayers = await roomParticipationRepository.CountActivePlayers(request.RoomId, ct);

            if (activePlayers < 2)
            {
                logger.LogInformation("Cannot start match: room {RoomId} has too few players ({Count})", request.RoomId, activePlayers);

                return (null, StartRefusal.TooFewPlayers, false);
            }

            var players = room.IsTeamGame
                ? await roomParticipationRepository.GetParticipationsByRoomId(room.Id, ct)
                : [];

            // играют команды с активными игроками, нужно хотя бы две
            if (room.IsTeamGame && players.Where(p => p.IsActive).Select(p => p.TeamNumber).Distinct().Count() < MatchHelper.MinTeams)
            {
                logger.LogInformation("Cannot start match: team room {RoomId} has fewer than two non-empty teams", room.Id);

                return (null, StartRefusal.TooFewTeams, false);
            }

            var categoryIds = request.CategoryIds?.Distinct().ToList() ?? [];
            var questions = await quizContentRepository.GetQuestionsByCategoriesAsync(categoryIds, ct);

            // в следующих играх серии сначала ещё не заданные, повторы только когда вопросы тем кончились
            var used = room.GameNumber > 1 && room.SeriesId is { } seriesId
                ? await matchRepository.GetQuestionIdsOfSeriesAsync(seriesId, ct)
                : [];

            // общая куча вопросов выбранных тем, случайные без повторов, из одной темы может выпасть сколько угодно
            var selected = questions
                .OrderBy(q => used.Contains(q.Id))
                .ThenBy(_ => Random.Shared.Next())
                .Take(request.QuestionCount)
                .ToList();

            if (selected.Count == 0)
            {
                logger.LogInformation("Cannot start match: no questions in categories {CategoryIds}", string.Join(", ", categoryIds));

                return (null, StartRefusal.NoQuestions, false);
            }

            var startedAt = UtcNow;
            var match = new MatchEntity
            {
                Id = Guid.NewGuid(),
                RoomId = room.Id,
                Status = MatchStatus.QuestionActive,
                CurrentQuestionIndex = 0,
                RoomMode = request.RoomMode,
                // подставим в следующую игру серии
                CategoryIds = categoryIds,
                RequestedQuestionCount = request.QuestionCount,
                StartedAt = startedAt,
                QuestionEndsAt = startedAt.AddSeconds(MatchHelper.GetQuestionDurationByRoomMode(request.RoomMode)),
                Questions = selected
                    .Select((question, index) => new MatchQuestionEntity
                    {
                        Id = Guid.NewGuid(),
                        QuestionId = question.Id,
                        Order = index,
                        // первый вопрос открыт сразу, остальные при переходе
                        OpenedAt = index == 0 ? startedAt : null
                    })
                    .ToList()
            };

            room.Status = RoomStatus.InProgress;

            // состав команд фиксируется, вышедшие из лобби сами и убранные в него не входят;
            // оборвавший связь сохраняет команду и может вернуться в окно после старта
            foreach (var player in players.Where(p => !p.IsActive && p.ExitReason != ExitReason.Disconnected))
            {
                player.TeamNumber = null;
            }

            try
            {
                await matchRepository.AddMatchAsync(match, ct);
                await matchRepository.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // версия комнаты сменилась между чтением и сохранением (вошли, пересели), StartMatchAsync повторит
                logger.LogInformation("Cannot start match: room {RoomId} changed concurrently, retrying", room.Id);

                return (null, StartRefusal.Concurrent, true);
            }
            catch (DbUpdateException)
            {
                logger.LogInformation("Cannot start match: room {RoomId} already has an active match", room.Id);

                return (null, StartRefusal.Concurrent, false);
            }

            logger.LogInformation("Match {MatchId} started in room {RoomId} with {Count} questions", match.Id, room.Id, match.Questions.Count);

            return (Publish(match), StartRefusal.None, false);
        }

        public async Task<CurrentQuestionResponse?> GetCurrentQuestionAsync(Guid matchId, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchAsync(matchId, ct);

            // вопрос видят только участники комнаты
            var me = match is null ? null : await FindMeAsync(match.RoomId, ct);

            if (match is null || me is null)
            {
                return null;
            }

            var currentQuestion = await matchRepository.GetMatchQuestionAsync(matchId, match.CurrentQuestionIndex, ct);

            if (currentQuestion is null)
            {
                return null;
            }

            return currentQuestion.ToCurrentQuestionResponse(match, me.Id, UtcNow);
        }

        // разбор только после финала, иначе подсказал бы верные ответы идущей игры
        public async Task<(MatchReviewResponse? Review, bool NotFinished)> GetReviewAsync(Guid matchId, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchAsync(matchId, ct);

            // видят все участники, и вышедшие тоже
            var me = match is null ? null : await FindMeAsync(match.RoomId, ct);

            if (match is null || me is null)
            {
                return (null, false);
            }

            if (match.Status != MatchStatus.Finished)
            {
                return (null, true);
            }

            var questions = await matchRepository.GetQuestionsForReviewAsync(matchId, ct);

            return (new MatchReviewResponse(matchId, questions.Select(q => q.ToQuestionReview(me.Id)).ToList()), false);
        }

        public async Task<bool> SubmitAnswerAsync(Guid matchId, SubmitAnswerRequest request, CancellationToken ct)
        {
            // время ответа фиксируем сразу, иначе после запросов в базу оно может оказаться позже закрытия
            var now = UtcNow;

            // под общим локом с закрытием вопроса: ответ либо войдёт в итог, либо не будет принят
            return await RunExclusiveAsync(matchId, () => TrySubmitAnswerAsync(matchId, request, now, ct), ct);
        }

        private async Task<bool> TrySubmitAnswerAsync(Guid matchId, SubmitAnswerRequest request, DateTime now, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchAsync(matchId, ct);

            if (match is null || match.Status != MatchStatus.QuestionActive || now > match.QuestionEndsAt)
            {
                return false;
            }

            var currentQuestion = match.Questions.FirstOrDefault(q => q.Order == match.CurrentQuestionIndex);

            if (currentQuestion is null)
            {
                return false;
            }

            // отвечает только активный участник этой комнаты
            var participant = await FindMeAsync(match.RoomId, ct);

            if (participant is null || !participant.IsActive)
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
                RoomPlayerId = participant.Id,
                SelectedOptionId = request.SelectedOptionId,
                AnsweredAt = now
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

            logger.LogInformation("Participant {RoomPlayerId} answered question {Order} of match {MatchId}", participant.Id, currentQuestion.Order, matchId);

            notifier.PlayerAnswered(new PlayerAnsweredEvent(match.RoomId, matchId, currentQuestion.Order, participant.Id));

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

                // ведущий закрывает когда захочет, даже если ответили не все
                await ShowAnswerAsync(match, UtcNow, ct);

                await matchRepository.SaveChangesAsync(ct);

                logger.LogInformation("Question {Order} of match {MatchId} closed", match.CurrentQuestionIndex, matchId);

                return Publish(match);
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

                return Publish(match);
            }, ct);
        }

        public Task<List<Guid>> GetExpiredMatchIdsAsync(CancellationToken ct)
            => matchRepository.GetExpiredMatchIdsAsync(UtcNow, ct);

        public Task<bool> AdvanceExpiredMatchAsync(Guid matchId, CancellationToken ct)
            => RunExclusiveAsync(matchId, () => AdvanceIfStillExpiredAsync(matchId, ct), ct);

        // под локом перепроверяем вдруг матч уже сдвинули руками
        private async Task<bool> AdvanceIfStillExpiredAsync(Guid matchId, CancellationToken ct)
        {
            var match = await matchRepository.GetMatchAsync(matchId, ct);

            var isRunning = match?.Status is MatchStatus.QuestionActive or MatchStatus.QuestionClosed;

            if (match is null || !isRunning || UtcNow <= match.QuestionEndsAt)
            {
                return false;
            }

            // время вышло: сначала показываем ответ, следующий вопрос после паузы
            if (match.Status == MatchStatus.QuestionActive)
            {
                await ShowAnswerAsync(match, match.QuestionEndsAt, ct);
            }
            else
            {
                await AdvanceMatchAsync(match, ct);
            }

            await matchRepository.SaveChangesAsync(ct);

            logger.LogInformation("Match {MatchId} auto-advanced to status {Status}, question {Order}", match.Id, match.Status, match.CurrentQuestionIndex);

            Publish(match);

            return true;
        }

        // клиенты узнают о смене вопроса сразу, без опроса сервера
        private MatchStateResponse Publish(MatchEntity match)
        {
            var state = match.ToStateResponse();

            notifier.MatchUpdated(state);
            // таблица команд меняется после закрытия вопроса
            notifier.RoomUpdated(match.RoomId);

            return state;
        }

        private async Task AdvanceMatchAsync(MatchEntity match, CancellationToken ct)
        {
            var isLast = match.CurrentQuestionIndex + 1 >= match.Questions.Count;

            // на финише комнату грузим раньше участников: выход или возврат после этого чтения
            // меняет версию комнаты, и сохранение откатится вместо очков по старому составу
            var room = isLast ? await roomRepository.GetRoomByIdAsync(match.RoomId, ct) : null;

            // по таймеру вопрос закрылся в дедлайн, досрочно уже закрыт
            var current = match.Questions.First(q => q.Order == match.CurrentQuestionIndex);

            if (current.ClosedAt is null)
            {
                await CloseQuestionAsync(match, current, match.QuestionEndsAt, ct);
            }

            if (!isLast)
            {
                var openedAt = UtcNow;

                match.CurrentQuestionIndex++;
                match.Status = MatchStatus.QuestionActive;
                match.QuestionEndsAt = openedAt.AddSeconds(MatchHelper.GetQuestionDurationByRoomMode(match.RoomMode));
                // от этого момента считаем время ответа на новый вопрос
                match.Questions.First(q => q.Order == match.CurrentQuestionIndex).OpenedAt = openedAt;

                return;
            }

            match.Status = MatchStatus.Finished;
            match.FinishedAt = UtcNow;

            await ApplyFinalScoresAsync(match, ct);

            if (room is not null)
            {
                room.Status = RoomStatus.Finished;
            }
        }

        // ответы больше не принимаются, все видят правильный ответ, через паузу таймер двигает матч
        private async Task ShowAnswerAsync(MatchEntity match, DateTime closedAt, CancellationToken ct)
        {
            var current = match.Questions.First(q => q.Order == match.CurrentQuestionIndex);

            match.Status = MatchStatus.QuestionClosed;
            await CloseQuestionAsync(match, current, closedAt, ct);
            match.QuestionEndsAt = UtcNow.AddSeconds(MatchHelper.RevealSeconds);
        }

        // запоминаем, кто был в игре на вопросе: активные на закрытии и вышедшие уже после открытия,
        // иначе выйти и вернуться выгоднее, чем ошибиться; вышедший до открытия команду не тянет
        private async Task CloseQuestionAsync(MatchEntity match, MatchQuestionEntity question, DateTime closedAt, CancellationToken ct)
        {
            var participants = await roomParticipationRepository.GetParticipationsByRoomId(match.RoomId, ct);
            var openedAt = question.OpenedAt ?? closedAt;

            question.ClosedAt = closedAt;
            question.InGamePlayerIds = participants
                .Where(p => p.IsActive || p.FinishedAt >= openedAt)
                .Select(p => p.Id)
                .ToList();
        }

        private async Task ApplyFinalScoresAsync(MatchEntity match, CancellationToken ct)
        {
            var correctCounts = await matchRepository.GetCorrectAnswerCountsAsync(match.Id, ct);

            var participants = await roomParticipationRepository.GetParticipationsByRoomId(match.RoomId, ct);

            // после финиша вернуться нельзя: ноль только у того, чьё окно возврата истекло ещё до финиша
            foreach (var participant in participants)
            {
                participant.Score = RoomMapper.KeepsScore(participant, match.FinishedAt!.Value)
                    ? correctCounts.GetValueOrDefault(participant.Id)
                    : 0;
            }
        }

        private Task<RoomPlayerEntity?> FindMeAsync(Guid roomId, CancellationToken ct)
            => roomParticipationRepository.FindParticipant(
                roomId, currentUser.RoomPlayerLinkId, currentUser.KeycloakSubject, ct);

        private async Task<T> RunExclusiveAsync<T>(Guid matchId, Func<Task<T>> action, CancellationToken ct)
        {
            var gate = matchLocks.GetLock(matchId);

            await gate.WaitAsync(ct);

            try
            {
                return await action();
            }
            catch (DbUpdateConcurrencyException)
            {
                // матч уже сдвинул другой инстанс
                logger.LogInformation("Match {MatchId} was changed concurrently, skipped", matchId);

                return default!;
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
