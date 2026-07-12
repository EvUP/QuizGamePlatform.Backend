using QuizGamePlatform.Backend.Application.Abstractions;
using QuizGamePlatform.Backend.Application.Contracts.Player;
using QuizGamePlatform.Backend.Application.Mappers;
using QuizGamePlatform.Backend.Core.Abstractions;

namespace QuizGamePlatform.Backend.Application.Services
{
    public class PlayerService(
        IPlayerRepository repository,
        ILogger<PlayerService> logger) : IPlayerService
    {
        public async Task<CreatePlayerResponse?> CreatePlayer(CreatePlayerRequest playerRequest, CancellationToken ct)
        {
            var player = await repository.CreatePlayerAsync(playerRequest.Username, ct);

            if (player is null)
            {
                logger.LogInformation("Player with username:{username} alreadyExist", playerRequest.Username);

                return null;
            }

            logger.LogInformation("Player with Id:{id} was created", player.Id);

            return player.ToCreatePlayerResponse();
        }
    }
}