using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Repositories
{
    public class RoomPatricipationRepository(ApplicationDbContext context) : IRoomParticipationRepository
    {
        public async Task<RoomPlayerEntity> CreateRoomPlayer(PlayerEntity player, RoomEntity room, CancellationToken ct)
        {
            var roomPlayer = new RoomPlayerEntity
            {
                Id = Guid.NewGuid(),

                Player = player,
                PlayerId = player.Id,
                JoinedAt = DateTime.UtcNow,
                IsActive = true,
                Score = 0,

                Room = room,
                RoomId = room.Id,
            };

            await context.RoomParticipations.AddAsync(roomPlayer, ct);

            return roomPlayer;
        }

        public async Task<RoomPlayerEntity?> GetRoomPlayerById(Guid roomId, Guid playerId, CancellationToken ct)
        {
            return await context.RoomParticipations
                 .FirstOrDefaultAsync(rp => rp.PlayerId == playerId && rp.RoomId == roomId && rp.IsActive, ct);
        }

        public async Task<List<RoomPlayerEntity>> GetParticipationsByRoomId(Guid roomId, CancellationToken ct)
        {
            return await context.RoomParticipations
        .Include(rp => rp.Room)
        .Include(rp => rp.Player)        
        .Where(rp => rp.RoomId == roomId)
        .ToListAsync(ct);
        }
    }

}