using testBdControllers.Application.Contracts.Room;
using testBdControllers.DataAccess.Entities;

namespace testBdControllers.Core.Abstractions
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