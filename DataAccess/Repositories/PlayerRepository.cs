using Microsoft.EntityFrameworkCore;
using QuizGamePlatform.Backend.Core.Abstractions;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.DataAccess.Repositories
{
    public class PlayerRepository(ApplicationDbContext context) : IPlayerRepository
    {
        public async Task<PlayerEntity> GetOrCreatePlayerAsync(string username, CancellationToken ct)
        {
            //todo Создается пока что по username
            var player = await context.Players.FirstOrDefaultAsync(p => p.UserName == username, ct);

            if (player is not null)
            {
                return player;
            }

            var newPlayer = new PlayerEntity
            {
                Id = Guid.NewGuid(),
                UserName = username
            };

            await context.Players.AddAsync(newPlayer, ct);
            //Сохранение будет производится на уровне RoomService
           
            return newPlayer;
        }
    }
}