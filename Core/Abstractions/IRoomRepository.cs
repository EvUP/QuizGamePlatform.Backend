using QuizGamePlatform.Backend.DataAccess.Entities;

namespace QuizGamePlatform.Backend.Core.Abstractions
{
    public interface IRoomRepository
    {
        /// <summary>
        /// Создать комнату
        /// </summary>
        Task<RoomEntity> CreateRoomAsync(string roomCode, CancellationToken ct);
        /// <summary>
        /// Найти комнату по ID
        /// </summary>
        Task<RoomEntity?> GetRoomByIdAsync(Guid Id, CancellationToken ct);
        /// <summary>
        /// Найти все существующие комнаты
        /// </summary>
        /// 
        Task<RoomEntity?> GetRoomByRoomCodeAsync(string roomCode, CancellationToken ct);
        Task<List<RoomEntity>> GetAllExistingRoomsAsync(CancellationToken ct);
        /// <summary>
        /// Удалить существующую комнату по Id
        /// </summary>
        Task<bool> DeleteExistingRoom(Guid id, CancellationToken ct);

        /// <summary>
        /// Найти участника в комнате
        /// </summary>
        Task<RoomPlayerEntity?> GetRoomParticipation(Guid roomId, Guid playerId, CancellationToken ct);
    };
}