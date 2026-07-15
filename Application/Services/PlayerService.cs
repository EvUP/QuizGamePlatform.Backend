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
      
    }
}