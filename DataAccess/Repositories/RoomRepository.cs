using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Repositories
{
    public class RoomRepository(ApplicationDbContext context) : IRoomRepository
    {
        public async Task<RoomEntity> CreateRoom(CancellationToken ct)
        {
            var room = new RoomEntity
            {
                RoomCode = Guid.NewGuid(),
                Status = RoomStatus.Waiting,
                CreatedAt = DateTime.UtcNow
            };

            await context.Rooms.AddAsync(room, ct);
            await context.SaveChangesAsync(ct);

            return room;
        }

        public async Task<RoomEntity> GetRoomById(Guid Id, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}