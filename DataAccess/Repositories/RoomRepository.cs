using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Application.Enums;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Repositories
{
    public class RoomRepository(ApplicationDbContext context) : IRoomRepository
    {
        public async Task<RoomEntity> CreateRoomAsync(string roomCode, UserEntity host, CancellationToken ct)
        {
            var room = new RoomEntity
            {
                Id = Guid.NewGuid(),
                RoomCode = roomCode,
                Status = RoomStatus.Waiting,
                CreatedAt = DateTime.UtcNow,
                HostUser = host,
                HostUserId = host.Id
            };

            await context.Rooms.AddAsync(room, ct);

            return room;
        }

        // владелец управляет, только пока сам в комнате
        public async Task<bool> IsHost(Guid roomId, string keycloakSubject, CancellationToken ct)
            => await context.Rooms.AnyAsync(r => r.Id == roomId
                && r.HostUser!.KeycloakSubject == keycloakSubject
                && r.Players.Any(p => p.UserId == r.HostUserId && p.IsActive), ct);

        public async Task<RoomEntity?> GetRoomByIdAsync(Guid id, CancellationToken ct)
            => await context.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);

        public async Task<RoomEntity?> GetRoomByRoomCodeAsync(string roomCode, CancellationToken ct)
            => await context.Rooms.FirstOrDefaultAsync(r => r.RoomCode == roomCode, ct);

        public async Task<List<RoomEntity>> GetRoomsOfUser(string keycloakSubject, CancellationToken ct)
            => await context.Rooms
                .Where(r => r.Players.Any(p => p.User!.KeycloakSubject == keycloakSubject))
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct);

        public void RemoveRoom(RoomEntity room)
            => context.Rooms.Remove(room);

        public void MarkChanged(RoomEntity room)
            => context.Entry(room).State = EntityState.Modified;
    }

}