using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Repositories
{
    public class RoomRepository(ApplicationDbContext context) : IRoomRepository
    {
        public async Task<RoomEntity> CreateRoomAsync(string roomCode, CancellationToken ct)
        {
            var room = new RoomEntity
            {
                Id = Guid.NewGuid(),
                RoomCode = roomCode,
                Status = RoomStatus.Waiting,
                CreatedAt = DateTime.UtcNow
            };

            await context.Rooms.AddAsync(room, ct);
            await context.SaveChangesAsync(ct);

            return room;
        }

        public async Task<RoomEntity?> GetRoomByIdAsync(Guid id, CancellationToken ct)
            => await context.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);

        public async Task<RoomEntity?> GetRoomByRoomCodeAsync(string roomCode, CancellationToken ct)
            => await context.Rooms.FirstOrDefaultAsync(r => r.RoomCode == roomCode, ct);

        public async Task<List<RoomEntity>> GetAllExistingRoomsAsync(CancellationToken ct)
            => await context.Rooms.ToListAsync(ct);

        public async Task<bool> DeleteExistingRoom(Guid id, CancellationToken ct)
        {
            var room = await context.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (room is null) return false;

            context.Rooms.Remove(room);
            await context.SaveChangesAsync(ct);

            return true;
        }
    }

}