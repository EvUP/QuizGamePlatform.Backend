using System.Numerics;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Core.Helpers;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Application.Mappers
{
    public static class RoomMapper
    {
        public static CreateRoomResponse ToCreateRoomResponse(
            this RoomEntity room,
            string? joinLink,
            List<TeamLinkResponse>? teams = null)
        {
            return new CreateRoomResponse(
                Id: room.Id,
                RoomCode: room.RoomCode,
                Status: room.Status,
                CreatedAt: room.CreatedAt,
                IsTeamGame: room.IsTeamGame,
                JoinLink: joinLink,
                Teams: teams
            );
        }

        public static MyRoomResponse ToMyRoomResponse(
            this RoomEntity room,
            string? joinLink,
            List<TeamLinkResponse>? teams,
            MyRoomResult? result,
            bool isHost)
        {
            // в Players репозиторий кладёт только моё участие
            var me = room.Players.FirstOrDefault();

            return new MyRoomResponse(
                Id: room.Id,
                RoomCode: room.RoomCode,
                Status: room.Status,
                CreatedAt: room.CreatedAt,
                IsTeamGame: room.IsTeamGame,
                JoinLink: joinLink,
                Teams: teams,
                Result: result,
                GameNumber: room.GameNumber,
                SeriesId: room.SeriesId ?? room.Id,
                IsHost: isHost,
                HostName: room.HostUser?.UserName,
                MyTeamNumber: me?.TeamNumber,
                IsActive: me?.IsActive ?? false,
                MyExitReason: me?.ExitReason
            );
        }

        // итог из состояния комнаты: места и ничья уже посчитаны, как на экране итогов; не игравшему в матче места нет
        public static MyRoomResult ToMyRoomResult(this RoomStateResponse state, bool played)
        {
            var me = played ? state.Players.FirstOrDefault(p => p.RoomPlayerLinkId == state.MyRoomPlayerLinkId) : null;

            if (state.IsTeamGame)
            {
                var teams = (state.Teams ?? []).Where(t => t.Members > 0).ToList();
                var myTeam = teams.FirstOrDefault(t => t.Number == me?.TeamNumber);

                return new MyRoomResult(myTeam?.Place, myTeam?.Points, state.IsDraw ? null : teams.FirstOrDefault(t => t.Place == 1)?.Name, state.IsDraw);
            }

            return new MyRoomResult(me?.Place, me?.Score, state.IsDraw ? null : state.Players.FirstOrDefault(p => p.Place == 1)?.PlayerName, state.IsDraw);
        }

        // у двух первых по месту поровну очков и времени верных (в том числе все по 0), победителя нет
        public static bool LeadersTied(List<(int Points, long? TimeMs)> byPlace)
            => byPlace.Count > 1 && byPlace[1] == byPlace[0];

        // сыграл матч: остался до конца или вышел уже после старта, как в рейтинге
        public static bool PlayedMatch(RoomPlayerEntity player, DateTime startedAt)
            => player.IsActive || player.FinishedAt >= startedAt;

        // очки сохраняются у активного и у того, чьё окно возврата к финишу ещё не истекло
        public static bool KeepsScore(RoomPlayerEntity player, DateTime finishedAt)
            => player.IsActive || player.FinishedAt >= finishedAt.AddSeconds(-RoomHelper.ReconnectWindowSeconds);

        public static RoomResponse ToJoinRoomResponse(this RoomPlayerEntity playerEntity)
        {
            return new RoomResponse(
            RoomPlayerLinkId: playerEntity.Id,
            RoomId: playerEntity.RoomId,
            FinishedAt: playerEntity.FinishedAt,
            RoomCode: playerEntity.Room.RoomCode,
            RoomStatus: playerEntity.Room.Status,
            PlayerName: playerEntity.Nickname,
            JoinedAt: playerEntity.JoinedAt,
            IsActive: playerEntity.IsActive,
            TeamNumber: playerEntity.TeamNumber
        );
        }

        // место и время только в итогах обычной игры
        public static RoomParticipationResponse ToRoomParticipationResponse(
            this RoomPlayerEntity playerEntity,
            Guid? hostUserId,
            bool isCaptain = false,
            bool showScore = true,
            int? place = null,
            long? correctTimeMs = null)
        {
            return new RoomParticipationResponse(
            RoomPlayerLinkId: playerEntity.Id,
            PlayerName: playerEntity.Nickname,
            IsGuest: playerEntity.UserId is null,
            IsHost: playerEntity.UserId is not null && playerEntity.UserId == hostUserId,
            IsActive: playerEntity.IsActive,
            ExitReason: playerEntity.ExitReason,
            Score: showScore ? playerEntity.Score : null,
            TeamNumber: playerEntity.TeamNumber,
            IsCaptain: isCaptain,
            Place: place,
            CorrectTimeMs: correctTimeMs
        );
        }

        // points есть только после старта, тогда считаем места
        public static List<TeamResponse> ToTeamResponses(
            this List<RoomTeamEntity> teams,
            RoomStatus status,
            List<RoomPlayerEntity> players,
            Func<List<RoomPlayerEntity>, int>? points,
            Func<List<RoomPlayerEntity>, long?>? averageTime,
            Func<RoomTeamEntity, string?> joinLink,
            DateTime? startedAt = null)
        {
            var result = new List<TeamResponse>();

            foreach (var team in teams)
            {
                // в лобби считаем активных, после старта состав зафиксирован;
                // оборвавший связь до старта в составе, только если вернулся
                var members = players
                    .Where(p => p.TeamNumber == team.Number && (status == RoomStatus.Waiting
                        ? p.IsActive
                        : startedAt is not { } started || PlayedMatch(p, started)))
                    .ToList();

                // пустая команда не играла
                if (status != RoomStatus.Waiting && members.Count == 0)
                {
                    continue;
                }

                var captain = members.Where(p => p.IsActive).MinBy(p => p.TeamJoinedAt);

                result.Add(new TeamResponse(
                    team.Number,
                    team.Name,
                    members.Count,
                    team.ExpectedPlayers,
                    captain?.Id,
                    joinLink(team),
                    points?.Invoke(members),
                    null,
                    averageTime?.Invoke(members)));
            }

            if (points is null)
            {
                return result;
            }

            // место не делим: при равных очках выше команда, которая в среднем быстрее отвечала верно
            return result
                .OrderByDescending(t => t.Points)
                .ThenBy(t => t.AvgCorrectTimeMs ?? long.MaxValue)
                .ThenBy(t => t.Number)
                .Select((t, i) => t with { Place = i + 1 })
                .ToList();
        }

        // время ответа и число ответов игрока по этим вопросам
        public sealed record AnswerTiming(List<long> CorrectMs, int Answered)
        {
            public static readonly AnswerTiming None = new([], 0);

            // сумма времени верных, null если верных нет
            public long? CorrectTimeMs => CorrectMs.Count > 0 ? CorrectMs.Sum() : null;
        }

        // время ответа от открытия вопроса; ответы должны быть с SelectedOption
        public static Dictionary<Guid, AnswerTiming> AnswerTimings(
            MatchEntity match,
            ICollection<MatchQuestionEntity> questions,
            List<PlayerAnswerEntity> answers)
        {
            var openedAt = questions.ToDictionary(q => q.Id, q => QuestionOpenedAt(match, q));

            return answers
                .Where(a => openedAt.ContainsKey(a.MatchQuestionId))
                .GroupBy(a => a.RoomPlayerId)
                .ToDictionary(
                    g => g.Key,
                    g => new AnswerTiming(
                        g.Where(a => a.SelectedOption.IsCorrect)
                            // открытие у старых матчей приблизительное, отрицательного времени не бывает
                            .Select(a => Math.Max(0, (long)(a.AnsweredAt - openedAt[a.MatchQuestionId]).TotalMilliseconds))
                            .ToList(),
                        g.Count()));
        }

        // среднее время верного ответа по всем игрокам команды, null если верных нет
        public static long? AverageCorrectTime(List<RoomPlayerEntity> members, Dictionary<Guid, AnswerTiming> timings)
        {
            var times = members.SelectMany(m => timings.GetValueOrDefault(m.Id)?.CorrectMs ?? []).ToList();

            return times.Count > 0 ? (long)Math.Round(times.Average()) : null;
        }

        // у матчей до OpenedAt: первый вопрос со старта, следующий после показа ответа на предыдущий
        private static DateTime QuestionOpenedAt(MatchEntity match, MatchQuestionEntity question)
        {
            if (question.OpenedAt is { } openedAt)
            {
                return openedAt;
            }

            var previous = match.Questions.FirstOrDefault(q => q.Order == question.Order - 1);

            return previous?.ClosedAt?.AddSeconds(MatchHelper.RevealSeconds) ?? match.StartedAt;
        }

        // за вопрос до 100: доля верных среди тех, кто был в игре на этом вопросе
        // ушедший до открытия вопроса не тянет команду вниз
        public static int TeamPoints(
            List<RoomPlayerEntity> members,
            ICollection<MatchQuestionEntity> questions,
            List<PlayerAnswerEntity> answers)
        {
            // сумма долей точной дробью: в double 62.5 + 83.33 + 16.67 бывает 162.4999 и команда теряет очко
            BigInteger numerator = 0;
            BigInteger denominator = 1;

            foreach (var question in questions)
            {
                var questionAnswers = answers.Where(a => a.MatchQuestionId == question.Id).ToList();

                var inGame = members
                    .Where(p => questionAnswers.Any(a => a.RoomPlayerId == p.Id) || question.InGamePlayerIds.Contains(p.Id))
                    .ToList();

                if (inGame.Count == 0)
                {
                    continue;
                }

                var correct = inGame.Count(p => questionAnswers.Any(a => a.RoomPlayerId == p.Id && a.SelectedOption.IsCorrect));

                numerator = numerator * inGame.Count + 100 * correct * denominator;
                denominator *= inGame.Count;

                var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
                numerator /= gcd;
                denominator /= gcd;
            }

            // округляем один раз, половина вверх
            return (int)((2 * numerator + denominator) / (2 * denominator));
        }

        public static LeaveRoomResponse ToLeaveRoomResponse(this RoomPlayerEntity roomPlayerEntity)
        {
            return new LeaveRoomResponse(
            RoomId: roomPlayerEntity.RoomId,
            RoomPlayerLinkId: roomPlayerEntity.Id,
            FinishedAt: roomPlayerEntity.FinishedAt ?? DateTime.UtcNow,
            ExitReason: roomPlayerEntity.ExitReason ?? ExitReason.Other
            );
        }
    }
}