using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Core.Abstractions
{
    public interface IPlayerRepository
    {
        /// <summary>
        /// Создать игрока
        /// </summary>
        Task<PlayerEntity?> CreatePlayerAsync(string username, CancellationToken ct);
        /// <summary>
        /// Присоединиться в комнату по RoomCode
        /// </summary>
        Task<bool> JoinToRoomByRoomCodeAsync(string roomCode, CancellationToken ct);
    };
}