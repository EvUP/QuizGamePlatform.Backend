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

        public async Task<RoomPlayerEntity?> FindByOriginAsync(Guid roomId, Guid originRoomPlayerId, CancellationToken ct)
            => await context.RoomParticipations
                .FirstOrDefaultAsync(rp => rp.RoomId == roomId && rp.OriginRoomPlayerId == originRoomPlayerId, ct);

        public async Task<int> CountActiveNonHostAsync(Guid roomId, Guid? hostUserId, CancellationToken ct)
            => await context.RoomParticipations
                .CountAsync(rp => rp.RoomId == roomId && rp.IsActive
                    && (rp.UserId == null || rp.UserId != hostUserId), ct);

        // ник хранится как ввели, занятость без учёта регистра: «Марина» и «марина» один ник
        public async Task<bool> IsNicknameTaken(Guid roomId, string nickname, CancellationToken ct)
        {
            var lowered = nickname.ToLowerInvariant();

            return await context.RoomParticipations
                .AnyAsync(rp => rp.RoomId == roomId && rp.Nickname.ToLower() == lowered, ct);
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

        // то же правило, что у состава команды на итогах и в рейтинге
        public async Task<Dictionary<Guid, HashSet<int>>> GetPlayedTeamNumbersAsync(IReadOnlyCollection<Guid> roomIds, CancellationToken ct)
        {
            var played = await context.RoomParticipations
                .Where(rp => roomIds.Contains(rp.RoomId) && rp.TeamNumber != null)
                .Where(rp => rp.IsActive || context.Matches.Any(m => m.RoomId == rp.RoomId && rp.FinishedAt >= m.StartedAt))
                .Select(rp => new { rp.RoomId, Number = rp.TeamNumber!.Value })
                .Distinct()
                .ToListAsync(ct);

            return played
                .GroupBy(x => x.RoomId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Number).ToHashSet());
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
                    Questions = context.MatchQuestions.Count(mq => mq.Match.RoomId == rp.RoomId),
                    FinishedAt = context.Matches.Where(m => m.RoomId == rp.RoomId).Select(m => m.FinishedAt).FirstOrDefault()
                })
                .GroupBy(x => new { x.UserId, x.UserName })
                .Select(g => new
                {
                    g.Key.UserName,
                    Score = g.Sum(x => x.Score),
                    Matches = g.Count(),
                    Questions = g.Sum(x => x.Questions),
                    // когда набрал нынешние очки: последняя игра с очками, без очков первая сыгранная
                    ReachedAt = g.Max(x => x.Score > 0 ? x.FinishedAt : null) ?? g.Min(x => x.FinishedAt)
                })
                // мест не делим: очки, потом точность, потом кто набрал раньше
                .OrderByDescending(e => e.Score)
                .ThenByDescending(e => e.Questions == 0 ? 0 : (double)e.Score / e.Questions)
                .ThenBy(e => e.ReachedAt)
                .ThenBy(e => e.UserName)
                .Select(e => new GlobalLeaderboardEntry(
                    e.UserName, e.Score, e.Matches, e.Questions == 0 ? 0 : e.Score * 100 / e.Questions))
                .ToListAsync(ct);
        }
    }
}
