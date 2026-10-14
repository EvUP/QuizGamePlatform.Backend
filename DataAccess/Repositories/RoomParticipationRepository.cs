using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Repositories
{
    public class RoomPatricipationRepository(ApplicationDbContext context) : IRoomParticipationRepository
    {
        public async Task<RoomPlayerEntity> CreateRoomPlayer(RoomEntity room, string nickname, UserEntity? user, CancellationToken ct)
        {
            var roomPlayer = new RoomPlayerEntity
            {
                Id = Guid.NewGuid(),

                Nickname = nickname,
                User = user,
                UserId = user?.Id,
                JoinedAt = DateTime.UtcNow,
                IsActive = true,
                Score = 0,

                Room = room,
                RoomId = room.Id,
            };

            await context.RoomParticipations.AddAsync(roomPlayer, ct);

            return roomPlayer;
        }

        public async Task<RoomPlayerEntity?> FindParticipant(Guid roomId, Guid? roomPlayerId, string? keycloakSubject, CancellationToken ct)
        {
            if (roomPlayerId is { } id)
            {
                return await context.RoomParticipations
                    .FirstOrDefaultAsync(rp => rp.RoomId == roomId && rp.Id == id, ct);
            }

            if (keycloakSubject is not null)
            {
                return await context.RoomParticipations
                    .FirstOrDefaultAsync(rp => rp.RoomId == roomId && rp.User!.KeycloakSubject == keycloakSubject, ct);
            }

            return null;
        }

        public async Task<bool> IsNicknameTaken(Guid roomId, string nickname, CancellationToken ct)
        {
            return await context.RoomParticipations
                .AnyAsync(rp => rp.RoomId == roomId && rp.Nickname == nickname, ct);
        }

        public async Task<int> CountActivePlayers(Guid roomId, CancellationToken ct)
        {
            return await context.RoomParticipations
                .CountAsync(rp => rp.RoomId == roomId && rp.IsActive, ct);
        }

        public async Task<int> CountTakenSlots(Guid roomId, CancellationToken ct)
        {
            return await context.RoomParticipations
                .CountAsync(rp => rp.RoomId == roomId
                    && (rp.IsActive || rp.UserId != null && rp.UserId == rp.Room.HostUserId), ct);
        }

        public async Task<List<RoomPlayerEntity>> GetParticipationsByRoomId(Guid roomId, CancellationToken ct)
        {
            return await context.RoomParticipations
                .Where(rp => rp.RoomId == roomId)
                .OrderByDescending(rp => rp.Score)
                .ThenBy(rp => rp.JoinedAt)
                .ToListAsync(ct);
        }

        public async Task<List<GlobalLeaderboardEntry>> GetLeaderboard(bool teamGames, CancellationToken ct)
        {
            return await context.RoomParticipations
                .Where(rp => rp.UserId != null && rp.Room.Status == RoomStatus.Finished && rp.Room.IsTeamGame == teamGames)
                // вышедший до старта матч не сыграл
                .Where(rp => rp.IsActive || context.Matches.Any(m => m.RoomId == rp.RoomId && rp.FinishedAt >= m.StartedAt))
                .Select(rp => new
                {
                    rp.UserId,
                    rp.User!.UserName,
                    rp.Score,
                    Questions = context.MatchQuestions.Count(mq => mq.Match.RoomId == rp.RoomId)
                })
                .GroupBy(x => new { x.UserId, x.UserName })
                .Select(g => new
                {
                    g.Key.UserName,
                    Score = g.Sum(x => x.Score),
                    Matches = g.Count(),
                    Questions = g.Sum(x => x.Questions)
                })
                .OrderByDescending(e => e.Score)
                .ThenBy(e => e.UserName)
                .Select(e => new GlobalLeaderboardEntry(
                    e.UserName, e.Score, e.Matches, e.Questions == 0 ? 0 : e.Score * 100 / e.Questions))
                .ToListAsync(ct);
        }
    };

}
