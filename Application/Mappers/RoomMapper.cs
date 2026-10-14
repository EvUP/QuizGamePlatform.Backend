using System.Numerics;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
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

        public static RoomParticipationResponse ToRoomParticipationResponse(
            this RoomPlayerEntity playerEntity,
            Guid? hostUserId,
            bool isCaptain = false,
            bool showScore = true)
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
            IsCaptain: isCaptain
        );
        }

        // points есть только после матча, тогда считаем места
        public static List<TeamResponse> ToTeamResponses(
            this List<RoomTeamEntity> teams,
            RoomStatus status,
            List<RoomPlayerEntity> players,
            Func<List<RoomPlayerEntity>, int>? points,
            Func<RoomTeamEntity, string?> joinLink)
        {
            var result = new List<TeamResponse>();

            foreach (var team in teams)
            {
                // в лобби считаем активных, после старта состав зафиксирован
                var members = players
                    .Where(p => p.TeamNumber == team.Number && (status != RoomStatus.Waiting || p.IsActive))
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
                    null));
            }

            if (points is null)
            {
                return result;
            }

            // равные очки делят место
            return result
                .Select(t => t with { Place = 1 + result.Count(o => o.Points > t.Points) })
                .OrderBy(t => t.Place)
                .ThenBy(t => t.Number)
                .ToList();
        }

        // за вопрос до 100: доля верных среди тех, кто был в игре на этом вопросе
        // ушедший не тянет команду вниз на вопросах, закрытых без него
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