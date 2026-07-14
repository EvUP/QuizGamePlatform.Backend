using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Core.Abstractions
{
    public interface IPlayerRepository
    {
        /// <summary>
        /// Создать игрока
        /// </summary>
        Task<PlayerEntity?> GetOrCreatePlayerAsync(string username, CancellationToken ct);
    };
}