using Microsoft.EntityFrameworkCore;
using testBdControllers.Application.Contracts.Room;
using testBdControllers.Application.Enums;
using testBdControllers.Core.Abstractions;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.DataAccess.Repositories
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