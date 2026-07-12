using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Repositories
{
    public class PlayerRepository(ApplicationDbContext context) : IPlayerRepository
    {
        public async Task<PlayerEntity?> CreatePlayerAsync(string username, CancellationToken ct)
        {
            //todo Создается пока что по username
            var player = await context.Players.FirstOrDefaultAsync(p => p.UserName == username, ct);

            if (player is not null)
            {
                return null;
            }

            var newPlayer = new PlayerEntity
            {
                Id = Guid.NewGuid(),
                UserName = username
            };

            await context.Players.AddAsync(newPlayer, ct);
            await context.SaveChangesAsync(ct);

            return newPlayer;
        }

        public Task<bool> JoinToRoomByRoomCodeAsync(string roomCode, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}