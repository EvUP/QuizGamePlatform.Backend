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

        // «Сыграть ещё» может и ведущий, ушедший посреди игры
        public async Task<bool> IsOwner(Guid roomId, string keycloakSubject, CancellationToken ct)
            => await context.Rooms.AnyAsync(r => r.Id == roomId
                && r.HostUser!.KeycloakSubject == keycloakSubject, ct);

        public async Task<RoomEntity?> GetNextRoomAsync(Guid roomId, CancellationToken ct)
            => await context.Rooms.FirstOrDefaultAsync(r => r.PreviousRoomId == roomId, ct);

        public async Task<RoomEntity?> GetLatestInSeriesAsync(Guid seriesId, CancellationToken ct)
            => await context.Rooms
                .Where(r => r.SeriesId == seriesId)
                .OrderByDescending(r => r.GameNumber)
                .FirstOrDefaultAsync(ct);

        // транзакция только через стратегию: при включённых повторах иначе EF не даст её открыть
        public async Task InTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct)
            => await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await context.Database.BeginTransactionAsync(ct);
                await work(ct);
                await tx.CommitAsync(ct);
            });

        public async Task<RoomEntity?> GetRoomByIdAsync(Guid id, CancellationToken ct)
            => await context.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);

        public async Task<RoomEntity?> GetRoomByRoomCodeAsync(string roomCode, CancellationToken ct)
            => await context.Rooms.FirstOrDefaultAsync(r => r.RoomCode == roomCode, ct);

        public async Task<List<RoomEntity>> GetRoomsOfUser(string keycloakSubject, CancellationToken ct)
            => await context.Rooms
                .AsNoTracking()
                .Include(r => r.HostUser)
                .Include(r => r.Teams)
                // из участников только своё, по нему итоги
                .Include(r => r.Players.Where(p => p.User!.KeycloakSubject == keycloakSubject))
                .Where(r => r.Players.Any(p => p.User!.KeycloakSubject == keycloakSubject))
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct);

        public async Task<List<RoomTeamEntity>> GetTeams(Guid roomId, CancellationToken ct)
            => await context.RoomTeams
                .Where(t => t.RoomId == roomId)
                .OrderBy(t => t.Number)
                .ToListAsync(ct);

        public async Task<RoomTeamEntity?> GetTeamByJoinCodeAsync(string joinCode, CancellationToken ct)
            => await context.RoomTeams
                .Include(t => t.Room)
                .FirstOrDefaultAsync(t => t.JoinCode == joinCode, ct);

        public async Task<(RoomEntity Room, RoomTeamEntity? Team)?> FindInviteAsync(string code, CancellationToken ct)
        {
            var room = await context.Rooms
                .AsNoTracking()
                .Include(r => r.HostUser)
                .FirstOrDefaultAsync(r => r.RoomCode == code, ct);

            if (room is not null)
            {
                return (room, null);
            }

            var team = await context.RoomTeams
                .AsNoTracking()
                .Include(t => t.Room)
                    .ThenInclude(r => r.HostUser)
                .FirstOrDefaultAsync(t => t.JoinCode == code, ct);

            return team is null ? null : (team.Room, team);
        }

        // коды комнат и команд живут в одном пространстве ссылок
        public async Task<bool> IsCodeTakenAsync(string code, CancellationToken ct)
            => await context.Rooms.AnyAsync(r => r.RoomCode == code, ct)
                || await context.RoomTeams.AnyAsync(t => t.JoinCode == code, ct);

        public void AddTeam(RoomTeamEntity team)
            => context.RoomTeams.Add(team);

        public void RemoveTeam(RoomTeamEntity team)
            => context.RoomTeams.Remove(team);

        public void ForgetChanges()
            => context.ChangeTracker.Clear();

        public void RemoveRoom(RoomEntity room)
            => context.Rooms.Remove(room);

        public void MarkChanged(RoomEntity room)
            => context.Entry(room).State = EntityState.Modified;
    }

}