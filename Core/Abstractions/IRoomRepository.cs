using QuizGamePlatform.Backend.Application.Contracts.Room;
using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Core.Abstractions
{
    public interface IRoomRepository
    {
        /// <summary>
        /// Создать комнату
        /// </summary>
        Task<RoomEntity> CreateRoom(CancellationToken ct);
        /// <summary>
        /// Найти комнату по ID
        /// </summary>
        Task<RoomEntity> GetRoomById(Guid Id, CancellationToken ct);
    };
}