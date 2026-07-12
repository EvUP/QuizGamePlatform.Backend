using QuizGamePlatform.Backend.Application.Contracts.Player;
using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Application.Mappers
{
    public static class PlayerMapper
    {
        public static CreatePlayerResponse ToCreatePlayerResponse(this PlayerEntity player)
        {
            return new CreatePlayerResponse(
                Id: player.Id,
                Username: player.UserName
            );
        }
    }
}