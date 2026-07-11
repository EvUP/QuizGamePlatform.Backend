using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Repositories
{
    public class RoomRepository(ApplicationDbContext context) : IRoomRepository
    {
        public async Task<RoomEntity> CreateRoomAsync(CancellationToken ct)
        {
            var room = new RoomEntity
            {
                Id = Guid.NewGuid(),
                RoomCode = Guid.NewGuid(),
                Status = RoomStatus.Waiting,
                CreatedAt = DateTime.UtcNow
            };

            await context.Rooms.AddAsync(room, ct);
            await context.SaveChangesAsync(ct);

            return room;
        }

        public async Task<RoomEntity?> GetRoomByIdAsync(Guid Id, CancellationToken ct)
        {
            var currentRoom = await context.Rooms.FirstOrDefaultAsync(r => r.Id == Id, ct);

            return currentRoom;
        }

        public async Task<List<RoomEntity>> GetAllExistingRoomsAsync(CancellationToken ct)
        {
            return await context.Rooms.ToListAsync(ct);
        }

        public async Task<bool> DeleteExistingRoom(Guid id, CancellationToken ct)
        {
            var room = await context.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);

            if (room is not null)
            {
                context.Rooms.Remove(room);
                await context.SaveChangesAsync(ct);

                return true;
            }

            return false;
        }
    }
}